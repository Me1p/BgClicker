using System;
using System.Diagnostics;
using System.Threading;

namespace BgClicker
{
    /// <summary>
    /// Runs clicks on a dedicated thread with Stopwatch-based pacing.
    /// timeBeginPeriod(1) keeps Thread.Sleep(1) close to one millisecond.
    /// </summary>
    public sealed class ClickEngine
    {
        private Thread _thread;
        private volatile bool _running;
        private volatile string _lastError;
        private long _count;

        public bool IsRunning
        {
            get { return _running; }
        }

        public long Count
        {
            get { return Interlocked.Read(ref _count); }
        }

        public string LastError
        {
            get { return _lastError; }
        }

        public bool Start(
            Func<bool> click,
            int clicksPerSecond,
            int jitterPercent,
            Action reload = null,
            int reloadSeconds = 0)
        {
            Stop();
            if (_thread != null)
            {
                // The previous thread did not finish in time.
                return false;
            }

            _count = 0;
            _lastError = null;
            _running = true;

            double baseInterval = Math.Max(1.0, 1000.0 / Math.Max(1, clicksPerSecond));
            int jitter = Math.Max(0, Math.Min(50, jitterPercent));

            _thread = new Thread(new ThreadStart(delegate
            {
                Run(click, baseInterval, jitter, reload, reloadSeconds);
            }));
            _thread.IsBackground = true;
            _thread.Name = "BgClicker";
            _thread.Priority = ThreadPriority.AboveNormal;
            _thread.Start();
            return true;
        }

        public void Stop()
        {
            _running = false;
            Thread thread = _thread;
            if (thread != null && thread.IsAlive)
            {
                thread.Join(2000);
            }

            if (thread == null || !thread.IsAlive)
            {
                _thread = null;
            }
        }

        private void Run(Func<bool> click, double baseInterval, int jitter, Action reload, int reloadSeconds)
        {
            Native.timeBeginPeriod(1);
            var random = new Random();
            var watch = Stopwatch.StartNew();
            double next = 0;
            double nextReload = reload != null && reloadSeconds > 0
                ? reloadSeconds * 1000.0
                : double.MaxValue;

            try
            {
                while (_running)
                {
                    if (reload != null && watch.Elapsed.TotalMilliseconds >= nextReload)
                    {
                        reload();
                        for (int i = 0; i < 15 && _running; i++)
                        {
                            Thread.Sleep(100);
                        }

                        nextReload = watch.Elapsed.TotalMilliseconds + (reloadSeconds * 1000.0);
                        continue;
                    }

                    if (!click())
                    {
                        _lastError = "Окно недоступно (закрыто или пересоздано)";
                        break;
                    }

                    Interlocked.Increment(ref _count);

                    double factor = 1.0;
                    if (jitter > 0)
                    {
                        factor += ((random.NextDouble() * 2.0) - 1.0) * (jitter / 100.0);
                    }

                    next += Math.Max(1.0, baseInterval * factor);
                    double now = watch.Elapsed.TotalMilliseconds;
                    if (next < now - 20.0)
                    {
                        next = now;
                    }

                    while (_running)
                    {
                        double remaining = next - watch.Elapsed.TotalMilliseconds;
                        if (remaining <= 0)
                        {
                            break;
                        }

                        if (remaining > 2.0)
                        {
                            Thread.Sleep(1);
                        }
                        else
                        {
                            Thread.SpinWait(80);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _lastError = ex.Message;
            }
            finally
            {
                _running = false;
                Native.timeEndPeriod(1);
                _thread = null;
            }
        }
    }
}
