using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

using System.Diagnostics;

namespace Swichter
{
	// Delegate matching the signature of EnumWindows callback function
	delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

	internal class WindowsAPI 
	{
		[DllImport("user32.dll", SetLastError = true)]
		public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

		// Importing EnumWindows from user32.dll
		[DllImport("user32.dll")]
		public static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

		// Importing GetWindowText from user32.dll
		[DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
		public static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

		// Importing GetWindowTextLength from user32.dll
		[DllImport("user32.dll", SetLastError = true)]
		public static extern int GetWindowTextLength(IntPtr hWnd);

		// Importing IsWindowVisible from user32.dll
		[DllImport("user32.dll")]
		public static extern bool IsWindowVisible(IntPtr hWnd);

		[DllImport("user32.dll")]
		public static extern bool EnumDesktopWindows(IntPtr hDesktop, EnumWindowsProc lpfn, IntPtr lParam);

		// Importing GetWindowLongPtr from user32.dll
		[DllImport("user32.dll", EntryPoint = "GetWindowLong")]
		public static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

		public const int GWL_STYLE = -16;
		public const int GWL_EXSTYLE = -20;

		public const int WS_CHILD = 0x40000000;
		public const int WS_VISIBLE = 0x10000000;

		public const int WS_EX_TOOLWINDOW = 0x00000080;
		public const int WS_EX_APPWINDOW = 0x00040000;
	}

	internal class WindowInteractions
	{
		public static string GetWindowText(IntPtr hWnd)
		{
			int length = WindowsAPI.GetWindowTextLength(hWnd);
			if (length < 1) return null;

			StringBuilder sb = new StringBuilder(length + 1);
			WindowsAPI.GetWindowText(hWnd, sb, sb.Capacity);
			string text = sb.ToString();

			return text.Length < 1 ? null : text;
		}
		public static uint? GetWindowPID(IntPtr hWnd)
		{
#pragma warning disable IDE0059 // Unnecessary assignment of a value
			uint processId = uint.MaxValue;
#pragma warning restore IDE0059 // Unnecessary assignment of a value
			WindowsAPI.GetWindowThreadProcessId(hWnd, out processId);
			if (processId == uint.MaxValue) return null;

			return processId;
		}

		public static int GetWindowStyle(IntPtr hWnd)
		{
			IntPtr style = WindowsAPI.GetWindowLongPtr(hWnd, WindowsAPI.GWL_STYLE);
			if (style == IntPtr.Zero) return 0;
			return style.ToInt32();
		}

		public static int GetWindowExStyle(IntPtr hWnd)
		{
			IntPtr exStyle = WindowsAPI.GetWindowLongPtr(hWnd, WindowsAPI.GWL_EXSTYLE);
			if (exStyle == IntPtr.Zero) return 0;
			return exStyle.ToInt32();
		}

		public static bool IsAltTabWindow(IntPtr hWnd)
		{
			{
				int style = GetWindowStyle(hWnd);
				if ((style & WindowsAPI.WS_CHILD) != 0)
					return false;
			}

			{
				int exStyle = GetWindowExStyle(hWnd);
				if ((exStyle & WindowsAPI.WS_EX_TOOLWINDOW) != 0)
					return false;
			}

			return true;
		}
	}
	
}
