using System;
using System.Configuration;
using System.Globalization;
using System.IO;
using System.Linq;
using System.ServiceModel;
using System.Text.RegularExpressions;
using Common;

namespace Client
{
    internal class Program
    {
        static void Main()
        {
            var cfgDataset = ConfigurationManager.AppSettings["DatasetRoot"] ?? ".\\Dataset";
            var datasetRoot = ResolveDatasetRoot(cfgDataset);
            var logPath = ConfigurationManager.AppSettings["LogPath"] ?? ".\\client-import.log";

            var logDir = Path.GetDirectoryName(Path.GetFullPath(logPath));
            if (!string.IsNullOrWhiteSpace(logDir)) Directory.CreateDirectory(logDir);

            Console.WriteLine("Config file : " + AppDomain.CurrentDomain.SetupInformation.ConfigurationFile);
            Console.WriteLine("Base dir    : " + AppDomain.CurrentDomain.BaseDirectory);
            Console.WriteLine("DatasetRoot : " + datasetRoot);
            Console.WriteLine("Log path    : " + Path.GetFullPath(logPath));
            Console.WriteLine();

            try
            {
                using (var log = new StreamWriter(logPath, append: true))
                {
                    log.WriteLine($"[{DateTime.UtcNow:o}] Start import from: {datasetRoot}");

                    if (!Directory.Exists(datasetRoot))
                    {
                        Console.WriteLine("Dataset folder not found at: " + datasetRoot);
                        log.WriteLine("[ERROR] Dataset not found.");
                        goto End;
                    }

                    var files = Directory.EnumerateFiles(datasetRoot, "*.csv", SearchOption.AllDirectories).ToList();
                    Console.WriteLine($"Found CSV files: {files.Count}");
                    foreach (var f in files.Take(5)) Console.WriteLine(" - " + f);
                    if (files.Count == 0) { log.WriteLine("[WARN] No CSV files."); goto End; }

                    ChannelFactory<IEisIngestService> factory = null;
                    IEisIngestService proxy = null;

                    try
                    {
                        factory = new ChannelFactory<IEisIngestService>("EisIngest");
                        proxy = factory.CreateChannel();

                        foreach (var csv in files)
                        {
                            try
                            {
                                var batteryId = ExtractBatteryId(csv);
                                var testId = ExtractTestId(csv);
                                var soc = ExtractSocPercent(Path.GetFileNameWithoutExtension(csv));

                                var lines = File.ReadAllLines(csv);
                                if (lines.Length == 0) { log.WriteLine($"[SKIP] Empty file: {csv}"); continue; }

                                var headerLine = lines.FirstOrDefault(l => !string.IsNullOrWhiteSpace(l));
                                if (string.IsNullOrEmpty(headerLine)) { log.WriteLine($"[SKIP] No header: {csv}"); continue; }

                                var firstData = lines.SkipWhile(string.IsNullOrWhiteSpace).Skip(1).FirstOrDefault();
                                var delim = DetectDelimiter(headerLine, firstData);
                                var headers = headerLine.Split(delim).Select(h => h.Trim()).ToArray();

                                var dataLines = lines.SkipWhile(string.IsNullOrWhiteSpace)
                                                     .Skip(1)
                                                     .Where(l => !string.IsNullOrWhiteSpace(l))
                                                     .ToList();

                                var planned = Math.Min(28, dataLines.Count);

                                var meta = new EisMeta
                                {
                                    BatteryId = batteryId,
                                    TestId = testId,
                                    SoCPercent = soc,
                                    FileName = Path.GetFileName(csv),
                                    TotalRows = planned
                                };

                                var startAck = proxy.StartSession(meta);
                                if (!startAck.Ok) { log.WriteLine($"[NACK] StartSession {csv}: {startAck.Message}"); continue; }
                                var sessionId = startAck.SessionId;

                                int sentOk = 0;
                                for (int i = 0; i < dataLines.Count; i++)
                                {
                                    EisSample sample; string reason;
                                    if (!TryParseSample(dataLines[i], i, headers, delim, out sample, out reason))
                                    {
                                        log.WriteLine($"[BAD] {csv} line {i}: {reason}");
                                        continue;
                                    }

                                    var r = proxy.PushSample(sessionId, sample);
                                    if (!r.Ok) log.WriteLine($"[REJECT] {csv} line {i}: {r.Message}");
                                    else sentOk++;
                                }

                                var endAck = proxy.EndSession(sessionId);
                                log.WriteLine($"[DONE] {csv} - accepted={sentOk}, planned={planned}, status={endAck.Status}");
                            }
                            catch (FaultException<ValidationFault> vf) { log.WriteLine($"[FAULT-VALID] {csv}: {vf.Detail.Reason}"); }
                            catch (FaultException<DataFormatFault> df) { log.WriteLine($"[FAULT-DATA] {csv}: {df.Detail.Reason}"); }
                            catch (Exception ex) { log.WriteLine($"[ERROR] {csv}: {ex.Message}"); }
                        }

                        try { ((IClientChannel)proxy)?.Close(); } catch { ((IClientChannel)proxy)?.Abort(); }
                        try { factory?.Close(); } catch { factory?.Abort(); }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine("FATAL: " + ex.Message);
                        log.WriteLine($"[FATAL] {ex}");
                        try { ((IClientChannel)proxy)?.Abort(); } catch { }
                        try { factory?.Abort(); } catch { }
                    }

                    log.WriteLine($"[{DateTime.UtcNow:o}] Import finished.");
                }
            }
            catch (Exception exOuter)
            {
                Console.WriteLine("UNHANDLED: " + exOuter.Message);
                Console.WriteLine(exOuter);
            }

        End:
            Console.WriteLine();
            Console.WriteLine("Gotovo. Proveri log: " + Path.GetFullPath(logPath));
            Console.WriteLine("Press ENTER to exit...");
            Console.ReadLine();
        }

        static string ResolveDatasetRoot(string cfgValue)
        {
            try
            {
                if (!string.IsNullOrEmpty(cfgValue))
                {
                    var p = Path.GetFullPath(cfgValue);
                    if (Directory.Exists(p)) return p;
                }
            }
            catch { }

            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            var rels = new[]
            {
                "Dataset",
                Path.Combine("..","..","Dataset"),
                Path.Combine("..","..","..","Dataset"),
                Path.Combine("..","..","..","..","Dataset"),
            };
            foreach (var rel in rels)
            {
                var cand = Path.GetFullPath(Path.Combine(baseDir, rel));
                if (Directory.Exists(cand)) return cand;
            }

            var candClient = Path.GetFullPath(Path.Combine(baseDir, @"..", "..", "..", "Client", "Dataset"));
            if (Directory.Exists(candClient)) return candClient;

            return Path.GetFullPath(cfgValue ?? ".\\Dataset");
        }

        static char DetectDelimiter(string headerLine, string firstDataLine)
        {
            if (!string.IsNullOrEmpty(headerLine))
            {
                if (headerLine.IndexOf(';') >= 0) return ';';
                if (headerLine.IndexOf(',') >= 0) return ',';
            }
            if (!string.IsNullOrEmpty(firstDataLine))
            {
                if (firstDataLine.IndexOf(';') >= 0) return ';';
                if (firstDataLine.IndexOf(',') >= 0) return ',';
            }
            return ','; // default
        }

        static bool TryParseSample(string line, int rowIndex, string[] headers, char delim, out EisSample s, out string reason)
        {
            s = null; reason = null;

            var parts = line.Split(delim);
            if (parts.Length < headers.Length) { reason = "Premalo kolona"; return false; }

            int idxFreq = Find(headers, "frequency(Hz)", "frequency", "freq", "hz");
            int idxR = Find(headers, "r(ohm)", "r(", "r ");
            int idxX = Find(headers, "x(ohm)", "x(", "x ");
            int idxT = Find(headers, "t(deg c)", "deg c", "degc", "temp", "temperature", "t(");
            int idxRange = Find(headers, "range(ohm)", "range");

            if (idxFreq < 0) { reason = "Nedostaje kolona Frequency(Hz)"; return false; }
            if (idxR < 0) { reason = "Nedostaje kolona R(ohm)"; return false; }
            if (idxX < 0) { reason = "Nedostaje kolona X(ohm)"; return false; }

            double f = Parse(parts[idxFreq]);
            double r = Parse(parts[idxR]);
            double x = Parse(parts[idxX]);
            double t = idxT >= 0 ? Parse(parts[idxT]) : 25.0;
            double range = idxRange >= 0 ? Parse(parts[idxRange]) : 0.0;

            if (double.IsNaN(f) || f <= 0) { reason = "Loša FrequencyHz"; return false; }
            if (double.IsNaN(r)) { reason = "Loša R_ohm"; return false; }
            if (double.IsNaN(x)) { reason = "Loša X_ohm"; return false; }
            if (double.IsNaN(t)) { reason = "Loša T_degC"; return false; }
            if (double.IsNaN(range) || range < 0) { reason = "Loša Range_ohm"; return false; }

            s = new EisSample
            {
                RowIndex = rowIndex,
                FrequencyHz = f,
                R_ohm = r,
                X_ohm = x,
                T_degC = t,
                Range_ohm = range,
                TimestampLocal = DateTime.Now
            };
            return true;
        }

        static int Find(string[] headers, params string[] keys)
        {
            for (int i = 0; i < headers.Length; i++)
            {
                var h = headers[i].Trim();
                foreach (var k in keys)
                {
                    if (h.IndexOf(k, StringComparison.OrdinalIgnoreCase) >= 0)
                        return i;
                }
            }
            return -1;
        }

        static double Parse(string v)
        {
            if (string.IsNullOrWhiteSpace(v)) return double.NaN;
            v = v.Trim();

            double d;
            if (double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out d)) return d;

            v = v.Replace(',', '.');
            if (double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out d)) return d;

            return double.NaN;
        }

        static string ExtractBatteryId(string path)
        {
            var m = Regex.Match(path, @"[\\/](B\d{1,3})[\\/]", RegexOptions.IgnoreCase);
            if (m.Success)
            {
                var digits = m.Groups[1].Value.Substring(1);
                int n; if (int.TryParse(digits, out n)) return "B" + n.ToString("00");
            }
            return "Bxx";
        }

        static string ExtractTestId(string path)
        {
            var m = Regex.Match(path, @"[\\/](Test[_\- ]?\d{1,2})[\\/]", RegexOptions.IgnoreCase);
            if (m.Success)
            {
                var num = Regex.Match(m.Groups[1].Value, @"\d+").Value;
                return "Test_" + num;
            }
            return "Test_1";
        }

        static int ExtractSocPercent(string fileNameNoExt)
        {
            var m1 = Regex.Match(fileNameNoExt, @"(\d{1,3})\s*%");
            if (m1.Success) return Clamp(int.Parse(m1.Groups[1].Value));

            var m2 = Regex.Match(fileNameNoExt, @"SoC(?:[_\-\s]*C)?[_\-\s]*(\d{1,3})(?=\D|$)", RegexOptions.IgnoreCase);
            if (m2.Success) return Clamp(int.Parse(m2.Groups[1].Value));

            return 0;

            int Clamp(int v) { if (v < 0) return 0; if (v > 100) return 100; return v; }
        }
    }
}
