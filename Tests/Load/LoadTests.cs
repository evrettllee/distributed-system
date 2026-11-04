using Shared;
using System.Collections.Concurrent;
using System.Diagnostics;
using Tests.Integration;
using Xunit;
using Xunit.Abstractions;

namespace Tests.Load
{
    [Trait("Category", "Load")]
    public class LoadTests
    {
        private readonly ITestOutputHelper _output;

        public LoadTests(ITestOutputHelper output)
        {
            _output = output;
        }

        // Нагрузочный тест — 20 клиентов одновременно, по 5 запросов каждый
        [Fact]
        public async Task Server_ManyClients_AllRequestsSucceed()
        {
            const int clients = 20;
            const int requestsPerClient = 5;

            ConcurrentBag<(double Ms, bool Ok, string? Error)> results = new();
            Stopwatch total = Stopwatch.StartNew();

            Task[] tasks = Enumerable.Range(1, clients).Select(_ => Task.Run(async () =>
            {
                for (int i = 0; i < requestsPerClient; i++)
                {
                    Stopwatch watch = Stopwatch.StartNew();
                    try
                    {
                        Response response = await TcpTestClient.SendAsync("GetStatus");
                        results.Add((watch.Elapsed.TotalMilliseconds, response.Status == "ok", response.Status == "ok" ? null : response.Message));
                    }
                    catch (Exception ex)
                    {
                        results.Add((watch.Elapsed.TotalMilliseconds, false, ex.Message));
                    }
                }
            })).ToArray();

            await Task.WhenAll(tasks);
            total.Stop();

            List<double> times = results.Select(x => x.Ms).OrderBy(x => x).ToList();
            int ok = results.Count(x => x.Ok);
            double p95 = times[Math.Max(0, (int)Math.Ceiling(0.95 * times.Count) - 1)];

            _output.WriteLine($"Клиентов: {clients}, запросов на клиента: {requestsPerClient}, всего: {results.Count}");
            _output.WriteLine($"Успешно: {ok}, ошибок: {results.Count - ok}");
            _output.WriteLine($"Общее время: {total.Elapsed.TotalSeconds:F2} сек, пропускная способность: {results.Count / total.Elapsed.TotalSeconds:F2} запросов/сек");
            _output.WriteLine($"Время ответа, мс: min {times[0]:F0} | avg {times.Average():F0} | p95 {p95:F0} | max {times[^1]:F0}");

            foreach (var group in results.Where(x => !x.Ok).GroupBy(x => x.Error))
                _output.WriteLine($"  ошибка x{group.Count()}: {group.Key}");

            double sequentialSeconds = clients * requestsPerClient * 2.0;

            Assert.Equal(results.Count, ok);
            Assert.True(total.Elapsed.TotalSeconds < sequentialSeconds / 4,
                $"Запросы обрабатываются не параллельно: {total.Elapsed.TotalSeconds:F1} сек при последовательных {sequentialSeconds:F0} сек");
        }
    }
}