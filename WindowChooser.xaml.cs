using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;

namespace Swichter
{
	/// <summary>
	/// Interaction logic for WindowChooser.xaml
	/// </summary>
	public partial class WindowChooser : Window
	{
		List<Process> _processList = null;
		List<Process> _excludeProcesses = null;

		public bool? ShowDialog(List<Process> excludeProcesses, bool topMost)
		{
			_excludeProcesses = excludeProcesses;
			Topmost = topMost;
			return ShowDialog();
		}

		public WindowChooser()
		{
			InitializeComponent();
		}

		private void PopulateProcessList()
		{
			_processList = new List<Process>();

			Process[] processes = Process.GetProcesses();
			List<string> windowTitles = new List<string>();

			foreach (Process process in processes)
			{
				if (_excludeProcesses != null && (_excludeProcesses.Find((Process p) => p.Id == process.Id) != null))
					continue;
				
				string windowTitle = process.MainWindowTitle;
				if (String.IsNullOrEmpty(windowTitle)) continue;

				_processList.Add(process);
				windowTitles.Add(windowTitle);

			}

			ListBox_Windows.ItemsSource = windowTitles;
		}

		private void Window_Loaded(object sender, RoutedEventArgs e)
		{
			PopulateProcessList();
        }

		private void Button_RefreshWindows_Click(object sender, RoutedEventArgs e)
		{
			PopulateProcessList();
		}

		private void Button_Okay_Click(object sender, RoutedEventArgs e)
		{
			DialogResult = true;
			Close();
		}

		private void Button_Cancel_Click(object sender, RoutedEventArgs e)
		{
			DialogResult = false;
			Close();
		}

		public Process GetSelectedProcess()
		{
			int index = ListBox_Windows.SelectedIndex;
			if (index < 0) return null;

			return _processList[index];
		}

		public bool Suspend()
		{
			bool? suspend = CheckBox_Suspend.IsChecked;
			if (suspend == null) return false;
			return suspend.Value;
		}
	}
}
