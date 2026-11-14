using System;
using System.Windows.Media;

namespace DesktopPop;

internal sealed class DesktopItem
{
	public string Key = "";

	public string Path = "";

	public string Name = "";

	public ImageSource? Icon;

	public bool IsVirtual;

	public bool IsFolder;

	public bool IsShortcut;

	public bool HasDuplicateName;

	public DateTime Stamp;

	public string Kind
	{
		get
		{
			if (!IsVirtual)
			{
				if (!IsFolder)
				{
					if (!IsShortcut)
					{
						return "파일";
					}
					return "바로가기";
				}
				return "폴더";
			}
			return "시스템 항목";
		}
	}

	public string DisplayLabel
	{
		get
		{
			if (!HasDuplicateName)
			{
				return Name;
			}
			return Name + " (" + Kind + ")";
		}
	}

	public string ToolTipText
	{
		get
		{
			if (!IsVirtual)
			{
				return $"{Name}\n{Kind}\n{Path}";
			}
			return Name;
		}
	}
}
