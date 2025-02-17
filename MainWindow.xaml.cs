using Microsoft.Win32.SafeHandles;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Timers;

using Timer = System.Timers.Timer;
using System.Threading;
using System.Runtime.InteropServices;

namespace Swichter
{
	class ProcessInfo
	{
		public string Name { get; private set; }
		public string PID { get; private set; }
		public string Status { get; set; }


		public ProcessInfo(Process p)
		{
			Name = p.MainWindowTitle;
			PID = p.Id.ToString();
			Status = "?";
		}

		public void Update(Process p)
		{
			int storedPid;
			if (!int.TryParse(PID, out storedPid) 
				|| storedPid != p.Id) return;

			Name = p.MainWindowTitle;
		}
	}

	/// <summary>
	/// Interaction logic for MainWindow.xaml
	/// </summary>
	public partial class MainWindow : Window
	{
		private readonly List<Process> _switchingProcesses = new List<Process>();
		private readonly ObservableCollection<ProcessInfo> _processInfos = new ObservableCollection<ProcessInfo>();
		private readonly List<ProcessMonitor> _processMonitors = new List<ProcessMonitor>();

		private uint _gameplayInterval = 30;
		private uint _randomInterval;
		private DateTime _intervalStart;
		private int _runningIndex = 0;
		private int _countdown;
		// private DateTime _intervalStarted;

		private readonly Timer _runningTimer;
		private readonly SynchronizationContext _synchronizationContext;

		public MainWindow()
		{
			InitializeComponent();
			DataGrid_ProcessQueueList.ItemsSource = _processInfos;
			TextBox_GameplayInterval.Text = _gameplayInterval.ToString();
			Slider_GameplayInterval.Value = _gameplayInterval;

			 _runningTimer = new Timer(5);
			_runningTimer.Elapsed += Timer_Elapsed;
			_runningTimer.AutoReset = true;

			_synchronizationContext = SynchronizationContext.Current;
		}

		private void Timer_Elapsed(object sender, ElapsedEventArgs e)
		{
			_synchronizationContext.Post(state =>
			{
				int processCount = _switchingProcesses.Count;
				if (processCount == 0)
				{
					_runningTimer.Stop();
					return;
				}

				TimeSpan intervalDiff = DateTime.Now - _intervalStart;

				int seconds = intervalDiff.Seconds;
				int countdown = (int)_randomInterval - seconds;
				if (countdown != _countdown)
				{
					_countdown = countdown;
					TextBox_Countdown.Text = _countdown.ToString();
				}

				if (seconds < _randomInterval) return;

				_randomInterval = IsRandomInterval() ?
					GetRandomInterval() : _gameplayInterval;

				_intervalStart = DateTime.Now;

				if (processCount == 1)
					return;

				ProcessMonitor oldpm = _processMonitors[_runningIndex];
				oldpm.Suspend();
				// possibly enter z order change here

				if (processCount == 2)
				{
					_runningIndex = 1 - _runningIndex;
				}
				else
				{
					int newIndex = _runningIndex + 1;

					if (IsRandomOrder())
					{
						newIndex = new Random().Next(processCount - 1);
						newIndex += Convert.ToInt32(newIndex >= _runningIndex);
					}
					else if (newIndex == processCount)
						newIndex = 0;

					_runningIndex = newIndex;
				}

				ResumeIndex(_runningIndex);
				return;
			}, null);
		}

		private void RunStop()
		{
			int processCount = _switchingProcesses.Count;
			if (processCount == 0)
			{
				Button_Run.IsEnabled = false;
				return;
			}

			if (_runningTimer.Enabled)
			{
				Button_Run.Content = "Run";

				_runningTimer.Stop();
			}
			else
			{
				Button_Run.Content = "Stop";

				foreach (ProcessMonitor p in _processMonitors)
				{
					p.Suspend();
				}

				if (IsRandomOrder())
					_runningIndex = new Random().Next(processCount);
				else
					_runningIndex = 0;

				_intervalStart = DateTime.Now;
				_randomInterval = IsRandomInterval() ?
					GetRandomInterval() : _gameplayInterval;

				ResumeIndex(_runningIndex);

				_runningTimer.Start();
			}
		}

		private void Button_ChooseWindow_Click(object sender, RoutedEventArgs e)
		{
			List<Process> excludedProcesses = new List<Process>(_switchingProcesses) 
			{
				Process.GetCurrentProcess()
			};

			WindowChooser chooser = new WindowChooser();
			bool? v = chooser.ShowDialog(excludedProcesses, CheckBox_KeepOnTop.IsChecked.Value);
			if (v == null || v == false) return;

			Process chosenProcess = chooser.GetSelectedProcess();

			if (_switchingProcesses.Find(p => p.Id == chosenProcess.Id) != null) return;

			Tuple<Process, ProcessInfo, ProcessMonitor> processStuff = AddProcess(chosenProcess);

			if (chooser.Suspend() == true)
			{
				ProcessMonitor pm = processStuff.Item3;
				pm.Suspend();
			}
		}

		private Tuple<Process, ProcessInfo, ProcessMonitor> AddProcess(Process p)
		{
			_switchingProcesses.Add(p);

			ProcessInfo info = new ProcessInfo(p);

			ProcessMonitor monitor = new ProcessMonitor(p);
			monitor.ProcessResumed += Monitor_ProcessResumed;
			monitor.ProcessSuspended += Monitor_ProcessSuspended;
			monitor.ProcessExited += Monitor_ProcessExited;

			monitor.Start();

			info.Status = monitor.IsSuspended() ? "Suspended" : "Running";

			_processMonitors.Add(monitor);
			_processInfos.Add(info);

			Button_Run.IsEnabled = true;

			return new Tuple<Process, ProcessInfo, ProcessMonitor>(p, info, monitor);
		}

		private void Monitor_ProcessSuspended(object sender, EventArgs e)
		{
			{
				ProcessMonitor monitor = (ProcessMonitor)sender;
				Process resumedProcess = monitor.MonitoredProcess;

				int index = _switchingProcesses.FindIndex(p => p.Id == resumedProcess.Id);
				if (index < 0) return;

				ProcessInfo i = _processInfos[index];
				i.Status = "Suspended";

				int oldIndex = DataGrid_ProcessQueueList.SelectedIndex;

				_processInfos.RemoveAt(index);
				_processInfos.Insert(index, i);

				if (oldIndex >= 0)
					DataGrid_ProcessQueueList.SelectedIndex = oldIndex;
			}

			UpdatePlayPauseButton();
		}

		private void Monitor_ProcessResumed(object sender, EventArgs e)
		{
			{
				ProcessMonitor monitor = (ProcessMonitor)sender;
				Process resumedProcess = monitor.MonitoredProcess;

				int index = _switchingProcesses.FindIndex(p => p.Id == resumedProcess.Id);
				if (index < 0) return;

				ProcessInfo i = _processInfos[index];
				i.Status = "Running";

				int oldIndex = DataGrid_ProcessQueueList.SelectedIndex;

				_processInfos.RemoveAt(index);
				_processInfos.Insert(index, i);

				if (oldIndex >= 0)
					DataGrid_ProcessQueueList.SelectedIndex = oldIndex;
			}

			UpdatePlayPauseButton();
		}

		private void UpdatePlayPauseButton()
		{
			int index = DataGrid_ProcessQueueList.SelectedIndex;
			if (index < 0) return;

			ProcessMonitor monitor = _processMonitors[index];
			if (monitor.IsSuspended())
			{
				Button_PlayPauseProcess.IsEnabled = true;
				Button_PlayPauseProcess.Content = "4"; //Webdings Play
			}
			else
			{
				Button_PlayPauseProcess.IsEnabled = true;
				Button_PlayPauseProcess.Content = ";"; //Webdings Pause
			}
		}

		private void Monitor_ProcessExited(object sender, EventArgs e)
		{
			Process endingProcess = (Process)sender;
			int index = _switchingProcesses.FindIndex(p => endingProcess.Id == p.Id);

			if (index < 0) return;

			RemoveProcessAt(index);
		}
		
		private void RemoveProcessAt(int index)
		{
			_switchingProcesses.RemoveAt(index);
			_processInfos.RemoveAt(index);

			ProcessMonitor monitor = _processMonitors[index];
			monitor.Stop();
			if (monitor.IsSuspended())
				monitor.Resume();

			_processMonitors.RemoveAt(index);

			int newCount = _switchingProcesses.Count;

			if (newCount == 0) {
				Button_Run.IsEnabled = false;
				return;
			}

			if (!_runningTimer.Enabled) return;

			if (_runningIndex < index) return;

			int oldRunningIndex = _runningIndex;
			_runningIndex = index - 1;
			if (_runningIndex < 0)
				_runningIndex = newCount - 1;

			if (oldRunningIndex == index)
				ResumeIndex(_runningIndex);
						
		}

		private void DataGrid_ProcessQueueList_SelectionChanged(object sender, SelectionChangedEventArgs e)
		{
			int index = DataGrid_ProcessQueueList.SelectedIndex;

			if (index < 0)
			{
				Button_MoveProcessUp.IsEnabled = false;
				Button_MoveProcessDown.IsEnabled = false;
				Button_PlayPauseProcess.IsEnabled = false;
				Button_RemoveProcess.IsEnabled = false;

				return;
			}

			int count = DataGrid_ProcessQueueList.Items.Count;

			Button_RemoveProcess.IsEnabled = true;
			Button_PlayPauseProcess.IsEnabled = true;

			Button_MoveProcessUp.IsEnabled = index != 0;

			Button_MoveProcessDown.IsEnabled = index < (count - 1);

			ProcessMonitor pm = _processMonitors[index];
			Button_PlayPauseProcess.Content = pm.IsSuspended() ? "4" //Webdings Play
				: ";"; //Webdings Pause
		}

		private void Button_RemoveProcess_Click(object sender, RoutedEventArgs e)
		{
			int index = DataGrid_ProcessQueueList.SelectedIndex;
			if (index < 0) return;

			RemoveProcessAt(index);
		}

		private void Button_PlayPauseProcess_Click(object sender, RoutedEventArgs e)
		{
			Button_PlayPauseProcess.IsEnabled = false;

			int index = DataGrid_ProcessQueueList.SelectedIndex;
			if (index < 0) return;

			ProcessMonitor pm = _processMonitors[index];
			if (pm.IsSuspended())
			{
				pm.Resume();
			}
			else
			{
				pm.Suspend();
			}
		}

		private void Slider_GameplayInterval_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
		{
			if (!IsVisible) return;

			uint slider_value = (uint)Slider_GameplayInterval.Value;
			if (slider_value == 0 || slider_value > 600)
			{
				Slider_GameplayInterval.Minimum = 1;
				Slider_GameplayInterval.Maximum = 600;

				Slider_GameplayInterval.Value = _gameplayInterval;
			}

			_gameplayInterval = slider_value;
			
			TextBox_GameplayInterval.Text = _gameplayInterval.ToString();
		}

		private void Window_Closed(object sender, EventArgs e)
		{
			_processMonitors.ForEach(pm =>
			{
				pm.Stop();
				pm.Resume();
			});
		}

		private void Button_MoveProcessUp_Click(object sender, RoutedEventArgs e)
		{
			int index = DataGrid_ProcessQueueList.SelectedIndex;
			if (index < 0) return;

			if (index == 0)
			{
				Button_MoveProcessUp.IsEnabled = false;
				return;
			}

			Process p = _switchingProcesses[index];
			ProcessInfo pinfo = _processInfos[index];
			ProcessMonitor pmon = _processMonitors[index];

			_switchingProcesses.RemoveAt(index);
			_processInfos.RemoveAt(index);
			_processMonitors.RemoveAt(index);

			int newIndex = index - 1;
			_switchingProcesses.Insert(newIndex, p);
			_processInfos.Insert(newIndex, pinfo);
			_processMonitors.Insert(newIndex, pmon);

			DataGrid_ProcessQueueList.SelectedIndex = newIndex;

			if (_runningIndex == index)
				_runningIndex = newIndex;
		}

		private void Button_MoveProcessDown_Click(object sender, RoutedEventArgs e)
		{
			int index = DataGrid_ProcessQueueList.SelectedIndex;
			if (index < 0) return;

			int count = DataGrid_ProcessQueueList.Items.Count;

			if (index == count - 1)
			{
				Button_MoveProcessDown.IsEnabled = false;
				return;
			}

			Process p = _switchingProcesses[index];
			ProcessInfo pinfo = _processInfos[index];
			ProcessMonitor pmon = _processMonitors[index];

			_switchingProcesses.RemoveAt(index);
			_processInfos.RemoveAt(index);
			_processMonitors.RemoveAt(index);

			int newIndex = index + 1;
			_switchingProcesses.Insert(newIndex, p);
			_processInfos.Insert(newIndex, pinfo);
			_processMonitors.Insert(newIndex, pmon);

			DataGrid_ProcessQueueList.SelectedIndex = newIndex;

			if (_runningIndex == index)
				_runningIndex = newIndex;
		}

		private bool IsRandomOrder()
		{
			bool? randomSwitching = CheckBox_RandomSwitching.IsChecked;
			if (randomSwitching.HasValue) return randomSwitching.Value;
			return false;
		}

		private bool IsRandomInterval()
		{
			bool? randomInterval = CheckBox_RandomInterval.IsChecked;
			if (randomInterval.HasValue) return randomInterval.Value;
			return false;
		}

		private uint GetRandomInterval()
		{
			uint plusminus = _gameplayInterval / 3;
			return (uint)new Random().Next((int)(_gameplayInterval - plusminus), (int)(_gameplayInterval + plusminus));
		}

		private void ResumeIndex(int index)
		{
			ProcessMonitor pm = _processMonitors[index];
			pm.BringToFront();
			pm.Resume();
			pm.SetOnTop();
		}

		private void Button_Run_Click(object sender, RoutedEventArgs e)
		{
			RunStop();
        }
    }
}
