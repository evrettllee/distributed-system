using System.Diagnostics;

namespace Tests.Integration
{
    public static class DockerCli
    {
        public static string Run(string arguments)
        {
            ProcessStartInfo info = new("docker", arguments)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };

            using Process process = Process.Start(info)
                ?? throw new InvalidOperationException("Не удалось запустить docker");

            string output = process.StandardOutput.ReadToEnd();
            string error = process.StandardError.ReadToEnd();
            process.WaitForExit();

            if (process.ExitCode != 0)
                throw new InvalidOperationException($"docker {arguments}: {error.Trim()}");

            return output.Trim();
        }

        public static async Task WaitHealthyAsync(string container, TimeSpan timeout)
        {
            await Task.Delay(TimeSpan.FromSeconds(5));
            DateTime deadline = DateTime.Now + timeout;

            while (DateTime.Now < deadline)
            {
                if (Run($"inspect -f {{{{.State.Health.Status}}}} {container}") == "healthy")
                    return;

                await Task.Delay(TimeSpan.FromSeconds(2));
            }

            throw new TimeoutException($"{container} не стал healthy за {timeout.TotalSeconds:0} сек");
        }
    }
}