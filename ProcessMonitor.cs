using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Diagnostics;
using System.Threading;
using System.Timers;

using Timer = System.Timers.Timer;
using System.Runtime.CompilerServices;

namespace Swichter
{

	internal class ProcessMonitor
	{
		[DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
		private static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

		// NtSuspendProcess declaration
		[DllImport("ntdll.dll", SetLastError = true)]
		private static extern int NtSuspendProcess(IntPtr processHandle);

		// NtResumeProcess declaration
		[DllImport("ntdll.dll", SetLastError = true)]
		private static extern int NtResumeProcess(IntPtr processHandle);

		[DllImport("user32.dll")]
		static extern bool SetForegroundWindow(IntPtr hWnd);

		[DllImport("user32.dll")]
		private static extern IntPtr GetForegroundWindow();

		[DllImport("user32.dll")]
		static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

		private const int HWND_TOPMOST = -1;
		private const int HWND_TOP = 0;
		private const int HWND_NOTOPMOST = -2;
		private const int HWND_BOTTOM = 1;

		private const int SWP_NOMOVE = 0x0002;
		private const int SWP_NOSIZE = 0x0001;
		
		private const uint WM_NULL = 0;

		private DateTime? _startTime;
		private readonly object _lock = new object();

		private readonly Process _monitoredProcess;
		private readonly Thread _monitoringThread;
		private readonly Timer _suspendTimer = new Timer(3000);
		private SynchronizationContext _synchronizationContext;

		private bool _continueThread = true;
		private bool _knownSuspended = false;

		public event EventHandler ProcessResumed;
		public event EventHandler ProcessSuspended;
		public event EventHandler ProcessExited;

		private void ThreadRunner()
		{
			// The goal of this thread is to indicate to our program whether the target process is suspended.
			// This is achieved by SENDing a WM_NULL message to the main window of the target process.
			// If the process is running, SendMessage returns immediately.
			// If the process is suspended, then SendMessage blocks until the main window thread can process the WM_NULL message.
			
			while (true)
			{
				lock (_lock)
				{
					if (_continueThread == false) break;
					_startTime = DateTime.Now;
				}

				IntPtr hwnd = _monitoredProcess.MainWindowHandle;
				SendMessage(hwnd, WM_NULL, IntPtr.Zero, IntPtr.Zero);
				
				// Execution here means the process is not suspended

				// prepare holder variables
				bool old_knownSuspended = false;
				EventHandler resumed;
				
				lock (_lock)
				{
					if (_continueThread == false) break;
					_startTime = null;
					old_knownSuspended = _knownSuspended;
					_knownSuspended = false;
					resumed = ProcessResumed;
				}

				// if the process was believed to be suspended and is now awake, then send an update that the process is running
				if (old_knownSuspended)
					_synchronizationContext.Post(state => resumed?.Invoke(this, EventArgs.Empty), null);

				// arbitrary sleep duration
				Thread.Sleep(3000);
			}
		}

		public bool IsSuspended()
		{
			DateTime startTime;
			lock (_lock)
			{
				// if we know the thread is suspended, then return that fact
				if (_knownSuspended) return true;

				// the _startTime variable is set in ThreadRunner just prior to calling SendMessage suspending.
				// Thus, if _startTime doesn't have a value, then we aren't suspended
				if (_startTime == null) return false;


				startTime = _startTime.Value;
				_knownSuspended = true;
			}

			// If the target process hasn't processed the WM_NULL message within 500 ms
			// then it can be assumed to be suspended.
			TimeSpan elapsed = DateTime.Now - _startTime.Value;
			return elapsed.TotalMilliseconds > 500;
		}

		public void BringToFront()
		{			
			DateTime started = DateTime.Now;
			lock (_lock)
			{
				IntPtr hwnd = _monitoredProcess.MainWindowHandle;

				IntPtr oldForegroundHwnd = GetForegroundWindow();

				while (true)
				{
					if ((DateTime.Now - started).Milliseconds > 1000)
						break;

					if (!SetForegroundWindow(hwnd)) continue;

					if (oldForegroundHwnd != GetForegroundWindow())
						break;

					Thread.Sleep(1);
				}
			}
		}

		public void SetOnTop()
		{
            IntPtr hwnd = _monitoredProcess.MainWindowHandle;
            if (false == SetWindowPos(hwnd, new IntPtr(HWND_TOPMOST), 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE))
            {
                Debug.Print("SetWindowPos failed?");
            }
        }

		public void Resume()
		{
			EventHandler processResumed;
			lock (_lock)
			{
				if (_knownSuspended == false) return;
				processResumed = ProcessResumed;

				IntPtr processHandle = _monitoredProcess.Handle;
				NtResumeProcess(processHandle);

				_startTime = null;
				_knownSuspended = false;
			}

			_synchronizationContext.Post(state => processResumed?.Invoke(this, EventArgs.Empty), null);
		}

		public void Suspend()
		{
			EventHandler processSuspended;
			lock (_lock)
			{
				if (_knownSuspended == true) return;

                if (false == SetWindowPos(_monitoredProcess.MainWindowHandle, new IntPtr(HWND_NOTOPMOST), 0, 0, 0, 0, SWP_NOSIZE | SWP_NOMOVE))
                {
                    Debug.Print("SetWindowPos failed on suspend? (send to back)");
                }

                processSuspended = ProcessSuspended;
				IntPtr processHandle = _monitoredProcess.Handle;
				NtSuspendProcess(processHandle);

				_knownSuspended = true;
			}

			_synchronizationContext.Post(state => processSuspended?.Invoke(this, EventArgs.Empty), null);
		}

		public Process MonitoredProcess { get { return _monitoredProcess; } }
		public Thread MonitoringThread { get { return _monitoringThread; } }
		public ProcessMonitor(Process p)
		{
			_synchronizationContext = SynchronizationContext.Current;

			_monitoredProcess = p;
			_monitoringThread = new Thread(ThreadRunner);

			_suspendTimer = new Timer();
			_suspendTimer.Interval = 3000;
			_suspendTimer.Elapsed += SuspendTimer;

			p.EnableRaisingEvents = true;
			p.Exited += Exited;
		}

		private void Exited(object sender, EventArgs e)
		{
			_synchronizationContext.Post(state => ProcessExited?.Invoke(sender, e), null);
		}

		private void SuspendTimer(object nothing, ElapsedEventArgs e)
		{
			if (_knownSuspended || !IsSuspended()) return;

			_knownSuspended = true;

			_synchronizationContext.Post(state => ProcessSuspended?.Invoke(this, EventArgs.Empty), null);
		}

		public void Start()
		{
			if (_monitoringThread.ThreadState != System.Threading.ThreadState.Unstarted) return;
			
			_monitoringThread.Start();
			_suspendTimer.Start();
		}

		public void Stop()
		{
			_continueThread = false;
			_suspendTimer.Stop();
		}
	}
}
