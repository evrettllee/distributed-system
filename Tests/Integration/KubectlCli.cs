using System.Diagnostics;
using System.Text;

namespace Tests.Integration
{
    public static class KubectlCli
    {
        public static string Run(string arguments)
        {
            ProcessStartInfo info = new("kubectl", arguments)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
                UseShellExecute = false
            };

            using Process process = Process.Start(info)
                ?? throw new InvalidOperationException("Не удалось запустить kubectl");

            string output = process.StandardOutput.ReadToEnd();
            string error = process.StandardError.ReadToEnd();
            process.WaitForExit();

            if (process.ExitCode != 0)
                throw new InvalidOperationException($"kubectl {arguments}: {error.Trim()}");

            return output.Trim();
        }

        public static string[] PodNames(string label) =>
            Run($"get pods -l {label} -o jsonpath={{.items[*].metadata.name}}")
                .Split(' ', StringSplitOptions.RemoveEmptyEntries);

        public static int ReadyReplicas(string deployment)
        {
            string value = Run($"get deployment {deployment} -o jsonpath={{.status.readyReplicas}}");
            return int.TryParse(value, out int ready) ? ready : 0;
        }

        public static async Task WaitReadyAsync(string deployment, string label, int expected, TimeSpan timeout)
        {
            DateTime deadline = DateTime.Now + timeout;

            while (DateTime.Now < deadline)
            {
                if (ReadyReplicas(deployment) == expected && PodNames(label).Length == expected)
                    return;

                await Task.Delay(TimeSpan.FromSeconds(2));
            }

            throw new TimeoutException($"{deployment} не достиг {expected} готовых Pod за {timeout.TotalSeconds:0} сек");
        }
    }
}