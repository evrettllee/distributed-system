using System.Diagnostics;

namespace Tests.Integration
{
    public sealed class PortForwardFixture : IDisposable
    {
        public const int LocalPort = 18080;

        private static readonly TimeSpan StartTimeout = TimeSpan.FromSeconds(20);

        private Process? _process;

        public PortForwardFixture() => Start();

        public void Restart()
        {
            Stop();
            Start();
        }

        public void Dispose() => Stop();

        private void Start()
        {
            ProcessStartInfo info = new("kubectl", $"port-forward service/education-server {LocalPort}:8080")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };

            using ManualResetEventSlim ready = new();

            Process process = new() { StartInfo = info };
            process.OutputDataReceived += (_, e) =>
            {
                if (e.Data?.StartsWith("Forwarding from") == true)
                    ready.Set();
            };
            process.ErrorDataReceived += (_, _) => { };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            _process = process;

            if (!ready.Wait(StartTimeout))
            {
                Stop();
                throw new TimeoutException($"port-forward не запустился за {StartTimeout.TotalSeconds:0} сек");
            }
        }

        private void Stop()
        {
            if (_process == null)
                return;

            if (!_process.HasExited)
            {
                _process.Kill(entireProcessTree: true);
                _process.WaitForExit();
            }

            _process.Dispose();
            _process = null;
        }
    }
}