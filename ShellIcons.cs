using System;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DesktopPop;

/// <summary>Windows 가 쓰는 아이콘 그림과 이름 읽기 (뒤에서 하나씩).</summary>
internal static class ShellIcons
{
	private static readonly Lazy<BlockingCollection<Action>> backgroundWork = new Lazy<BlockingCollection<Action>>(() =>
	{
		BlockingCollection<Action> queue = new BlockingCollection<Action>();
		Thread thread = new Thread(() =>
		{
			foreach (Action item in queue.GetConsumingEnumerable())
			{
				try
				{
					item();
				}
				catch (Exception ex)
				{
					App.Log(ex);
				}
			}
		});
		thread.IsBackground = true;
		thread.Name = "DesktopPop icon refresh";
		thread.SetApartmentState(ApartmentState.STA);
		thread.Start();
		return queue;
	});

	private static readonly Lazy<ImageSource> folderPlaceholder = new Lazy<ImageSource>(() => MakePlaceholder(folder: true));

	private static readonly Lazy<ImageSource> filePlaceholder = new Lazy<ImageSource>(() => MakePlaceholder(folder: false));

	internal static void QueueWork(Action action)
	{
		backgroundWork.Value.Add(action);
	}

	internal static ImageSource Placeholder(bool folder)
	{
		if (!folder)
		{
			return filePlaceholder.Value;
		}
		return folderPlaceholder.Value;
	}

	private static ImageSource MakePlaceholder(bool folder)
	{
		DrawingGroup drawingGroup = new DrawingGroup();
		SolidColorBrush brush = new SolidColorBrush(folder ? Color.FromRgb(223, 175, 66) : Color.FromRgb(164, 186, 207));
		Pen pen = new Pen(new SolidColorBrush(Color.FromRgb(73, 88, 106)), 2.0);
		if (folder)
		{
			drawingGroup.Children.Add(new GeometryDrawing(brush, pen, new RectangleGeometry(new Rect(5.0, 9.0, 22.0, 12.0), 3.0, 3.0)));
			drawingGroup.Children.Add(new GeometryDrawing(brush, pen, new RectangleGeometry(new Rect(5.0, 17.0, 54.0, 38.0), 4.0, 4.0)));
		}
		else
		{
			drawingGroup.Children.Add(new GeometryDrawing(brush, pen, new RectangleGeometry(new Rect(13.0, 5.0, 38.0, 54.0), 3.0, 3.0)));
		}
		DrawingImage drawingImage = new DrawingImage(drawingGroup);
		drawingImage.Freeze();
		return drawingImage;
	}

	[DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
	private static extern void SHCreateItemFromParsingName(string path, nint pbc, [In] ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out IShellItem item);

	public static (string? name, BitmapSource? icon) Load(string parsingName, int px)
	{
		IShellItem item = null;
		try
		{
			Guid riid = typeof(IShellItem).GUID;
			SHCreateItemFromParsingName(parsingName, IntPtr.Zero, ref riid, out item);
			string ppszName = null;
			try
			{
				item.GetDisplayName(0u, out ppszName);
			}
			catch
			{
			}
			BitmapSource icon = null;
			if (item is IShellItemImageFactory shellItemImageFactory && shellItemImageFactory.GetImage(new Native.SIZE
			{
				cx = px,
				cy = px
			}, 0, out var phbm) == 0 && phbm != IntPtr.Zero)
			{
				try
				{
					icon = FromHBitmap(phbm);
				}
				finally
				{
					Native.DeleteObject(phbm);
				}
			}
			return (name: ppszName, icon: icon);
		}
		catch
		{
			return (name: null, icon: null);
		}
		finally
		{
			if (item != null)
			{
				Marshal.ReleaseComObject(item);
			}
		}
	}

	private static BitmapSource? FromHBitmap(nint hbmp)
	{
		Native.GetObject(hbmp, Marshal.SizeOf<Native.BITMAP>(), out var bm);
		int bmWidth = bm.bmWidth;
		int height = Math.Abs(bm.bmHeight);
		if (bmWidth <= 0 || height <= 0)
		{
			return null;
		}
		Native.BITMAPINFO bi = new Native.BITMAPINFO
		{
			biSize = 40,
			biWidth = bmWidth,
			biHeight = -height, // 음수 = 위에서 아래로
			biPlanes = 1,
			biBitCount = 32
		};
		byte[] pixels = new byte[bmWidth * height * 4];
		nint dC = Native.GetDC(IntPtr.Zero);
		try
		{
			if (Native.GetDIBits(dC, hbmp, 0u, (uint)height, pixels, ref bi, 0u) == 0)
			{
				return null;
			}
		}
		finally
		{
			Native.ReleaseDC(IntPtr.Zero, dC);
		}
		// 투명도 정보가 하나도 없는(전부 0) 그림은 완전히 불투명한 그림으로 취급
		bool hasAlpha = false;
		for (int i = 3; i < pixels.Length; i += 4)
		{
			if (pixels[i] != 0)
			{
				hasAlpha = true;
				break;
			}
		}
		if (!hasAlpha)
		{
			for (int j = 3; j < pixels.Length; j += 4)
			{
				pixels[j] = byte.MaxValue;
			}
		}
		BitmapSource bitmapSource = BitmapSource.Create(bmWidth, height, 96.0, 96.0, hasAlpha ? PixelFormats.Pbgra32 : PixelFormats.Bgra32, null, pixels, bmWidth * 4);
		bitmapSource.Freeze();
		return bitmapSource;
	}
}
