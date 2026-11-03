// Prosty test obciążeniowy: N równoległych klientów wysyła żądania GET przez zadany czas.
// Użycie: dotnet run tools/LoadTest.cs -- <url> <liczba klientów> <sekundy> [token JWT]
#:property PublishAot=false
using System.Collections.Concurrent;
using System.Diagnostics;

var (url, clients, seconds) = (args[0], int.Parse(args[1]), int.Parse(args[2]));
using var http = new HttpClient();
if (args.Length > 3) http.DefaultRequestHeaders.Authorization = new("Bearer", args[3]);

await Run(TimeSpan.FromSeconds(3)); // rozgrzewka (kompilacja JIT, pula połączeń) - wyniki pomijane
var (latencies, errors) = await Run(TimeSpan.FromSeconds(seconds));

var sorted = latencies.Order().ToArray();
double Percentile(double p) => sorted[Math.Min(sorted.Length - 1, (int)(p * sorted.Length))];
Console.WriteLine($"{url} | klienci: {clients} | czas: {seconds} s");
Console.WriteLine($"żądań: {sorted.Length} ({sorted.Length / (double)seconds:F0}/s), błędów: {errors}");
Console.WriteLine($"czas odpowiedzi: p50 {Percentile(.50):F1} ms, p95 {Percentile(.95):F1} ms, p99 {Percentile(.99):F1} ms");

async Task<(ConcurrentBag<double>, int)> Run(TimeSpan duration)
{
    var results = new ConcurrentBag<double>();
    var failed = 0;
    var end = DateTime.UtcNow + duration;
    await Task.WhenAll(Enumerable.Range(0, clients).Select(async _ =>
    {
        while (DateTime.UtcNow < end)
        {
            var stopwatch = Stopwatch.StartNew();
            try
            {
                using var response = await http.GetAsync(url);
                await response.Content.ReadAsByteArrayAsync();
                if (!response.IsSuccessStatusCode) Interlocked.Increment(ref failed);
            }
            catch (HttpRequestException) { Interlocked.Increment(ref failed); }
            results.Add(stopwatch.Elapsed.TotalMilliseconds);
        }
    }));
    return (results, failed);
}
