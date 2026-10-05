// Copyright 2016-2019 Andreia Gaita
//
// This work is licensed under the terms of the MIT license.
// For a copy, see <https://opensource.org/licenses/MIT>.

using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;

namespace Unity.Editor.Tasks
{
	using System;
	using System.Threading;
	using Internal.IO;

	/// <summary>
	/// A process manager that configures processes for running and keeps track of running processes.
	/// </summary>
	public class ProcessManager : IProcessManager
	{
		private readonly HashSet<IProcess> processes = new HashSet<IProcess>();
		// 每次 git 子进程都在"发起它的那个线程"上同步 Add/Remove：ProcessWrapper.Run 里
		// Process.Start() 之后直接 onStart、Run() 收尾直接 onEnd，中间没有线程切换。
		// 而 Unity 侧同时存在后台线程（窗口轮询、RefreshWorkingStatus）与主线程两条调用路径，
		// 各自跑自己的 git 子进程 —— 所以这个集合必须自己保护自己，不能假定调用方串行。
		private readonly object processesLock = new object();

		/// <summary>
		/// Creates an instance of the process manager and the <see cref="DefaultProcessEnvironment"/>.
		/// </summary>
		/// <param name="environment"></param>
		public ProcessManager(IEnvironment environment)
		{
			DefaultProcessEnvironment = new ProcessEnvironment(environment);
		}

		/// <inheritdoc />
		public virtual T Configure<T>(T processTask, ProcessStartInfo startInfo)
			where T : IProcessTask
		{
			processTask.Configure(this, startInfo);

			processTask.OnStartProcess += p => {
				lock (processesLock) processes.Add(p);
			};
			processTask.OnEndProcess += p => {
				lock (processesLock)
				{
					if (processes.Contains(p))
						processes.Remove(p);
				}
			};
			return processTask;
		}

		/// <inheritdoc />
		public virtual T Configure<T>(T processTask, string workingDirectory = null)
				where T : IProcessTask
		{
			var startInfo = new ProcessStartInfo {
				RedirectStandardInput = true,
				RedirectStandardOutput = true,
				RedirectStandardError = true,
				UseShellExecute = false,
				CreateNoWindow = true,
				StandardOutputEncoding = Encoding.UTF8,
				StandardErrorEncoding = Encoding.UTF8
			};

			startInfo.FileName = processTask.ProcessName.ToSPath().ToString();
			startInfo.Arguments = processTask.ProcessArguments;
			startInfo.WorkingDirectory = workingDirectory;

			return Configure(processTask, startInfo);
		}

		/// <inheritdoc />
		public virtual BaseProcessWrapper WrapProcess(string taskName,
			ProcessStartInfo startInfo,
			IOutputProcessor outputProcessor,
			Action onStart,
			Action onEnd,
			Action<Exception, string> onError,
			CancellationToken token = default)
		{
			return new ProcessWrapper(taskName, startInfo, outputProcessor,
				onStart, onEnd, onError, token);
		}

		/// <inheritdoc />
		public virtual void Stop()
		{
			foreach (var p in SnapshotProcesses())
			{
				p.Stop();
			}
		}

		/// <summary>
		/// 加锁取快照：调用方在锁外遍历，避免 p.Stop()/p.Dispose() 的耗时（或阻塞）
		/// 卡住锁、进而阻塞其他线程的 Add/Remove。
		/// </summary>
		private IProcess[] SnapshotProcesses()
		{
			lock (processesLock)
			{
				return processes.ToArray();
			}
		}

		private bool disposed;
		/// <summary>
		/// Stop and clean up managed processes
		/// </summary>
		/// <param name="disposing"></param>
		protected virtual void Dispose(bool disposing)
		{
			if (disposed) return;
			if (disposing)
			{
				foreach (var p in SnapshotProcesses())
				{
					try
					{
						p.Stop();
						p.Dispose();
					}
					catch {}
				}

				disposed = true;
			}
		}

		/// <inheritdoc />
		public void Dispose()
		{
			Dispose(true);
		}

		/// <inheritdoc />
		public IProcessEnvironment DefaultProcessEnvironment { get; }
	}
}
