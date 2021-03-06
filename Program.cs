﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace PlayCZ2TvHeadend
{
    internal static class Program
    {
        private static List<Radio> _radios = new List<Radio>();

        private static void Main(string[] args)
        {
            Console.WriteLine("PlayCZ2TvHeadend Starting...");
            Console.WriteLine();
            GetRadios();
            GeneratePlaylist(0);
            GeneratePlaylist(1);
            GeneratePlaylist(2);
            Console.WriteLine();
            Console.WriteLine("Finished!");
        }

        private static void GetRadios()
        {
            Console.WriteLine("Getting Radio List...");
            var radios = DownloadToString("http://api.play.cz/xml/getRadios");

            var r = new Regex(
                @"<title>.+?CDATA\[(.+?)\]\]></title>\s<description>.+?CDATA\[(.+?)\]\]></description>\s<shortcut>.+?CDATA\[(.+?)\]\]></shortcut>",
                RegexOptions.Compiled);

            foreach (Match match in r.Matches(radios))
            {
                var radio = new Radio
                {
                    Title = match.Groups["1"].Value,
                    Description = match.Groups["2"].Value,
                    Shortcut = match.Groups["3"].Value,
                    StreamList = new List<string>(),
                    LogoUrl = $"http://api.play.cz/static/radio_logo/t200/{match.Groups["3"].Value}.png"
                };
                _radios.Add(radio);
            }

            Parallel.ForEach(_radios, radio =>
                {
                    radio.StreamList = GetStreams(radio.Shortcut).Distinct()
                        .OrderByDescending(sh => sh.Contains("aac"))
                        .ThenByDescending(sh => sh.Contains("mp3"))
                        .ThenByDescending(sh => sh.Contains("320"))
                        .ThenByDescending(sh => sh.Contains("256"))
                        .ThenByDescending(sh => sh.Contains("192"))
                        .ThenByDescending(sh => sh.Contains("160"))
                        .ThenByDescending(sh => sh.Contains("128"))
                        .ThenByDescending(sh => sh)
                        .ToList();
                }
            );
            Console.WriteLine("Sorting Radios ...");
            _radios = _radios.OrderBy(ra => ra.Title).ToList();
        }

        private static List<string> GetStreams(string shortcut)
        {
            Console.WriteLine("Thread: " + Thread.CurrentThread.ManagedThreadId + " Getting streams of " + shortcut);
            var result = new List<string>();
            var httpdata = DownloadToString("http://api.play.cz/xml/getAllStreams/" + shortcut);
            httpdata = httpdata.Replace("\r", string.Empty);
            httpdata = httpdata.Replace("\n", string.Empty);
            httpdata = httpdata.Replace("\t", string.Empty);
            var reg = new Regex("<streams>(.+?)</streams>");
            foreach (Match match in reg.Matches(httpdata))
            {
                var reg2 = new Regex(@"<(.+?)>(.+?)</loop></");
                foreach (Match match2 in reg2.Matches(match.Groups["1"].Value))
                {
                    var suffix = match2.Groups["1"].Value;
                    var bitrates = match2.Groups["2"].Value;
                    var reg3 = new Regex(@"<loop>.+?CDATA\[(.+?)\]\]></loop>");
                    foreach (Match match3 in reg3.Matches(bitrates + "</loop>"))
                    {
                        string res;
                        var bitrate = match3.Groups["1"].Value;
                        if (suffix.ToLowerInvariant() != "wma")
                        {
                            res = DownloadToString(
                                $"http://api.play.cz/plain/getStream/{shortcut}/{suffix}/{bitrate}");
                            res = res.Replace("#EXTM3U\n", string.Empty).Replace("#EXT-X-ENDLIST\n", string.Empty)
                                .Replace("#EXTM3U\n", string.Empty).Replace("\n#EXT-X-ENDLIST", string.Empty);
                        }

                        else
                        {
                            var httpdata2 =
                                DownloadToString($"http://api.play.cz/plain/getStream/{shortcut}/{suffix}/{bitrate}");
                            httpdata2 = httpdata2.Replace("\r", string.Empty);
                            httpdata2 = httpdata2.Replace("\n", string.Empty);
                            httpdata2 = httpdata2.Replace("\t", string.Empty);
                            //var reg4 = new Regex(@"<pubpoint>.+?CDATA\[(.+?)\]\]></pubpoint>");
                            var reg4 = new Regex("<ref href=\"(.+?)\">"); //(.+?)</ref>");
                            var sb = new StringBuilder();
                            foreach (Match match4 in reg4.Matches(httpdata2)) sb.AppendLine(match4.Groups["1"].Value);
                            res = sb.ToString();
                        }

                        result.AddRange(res.Split(Environment.NewLine.ToCharArray()));
                    }
                }
            }

            return result;
        }

        private static void GeneratePlaylist(int playlistType)
        {
            Console.WriteLine($"Generating PlayList type {playlistType}...");
            var result = new StringBuilder();
            result.AppendLine("#EXTM3U");
            var radiocount = 0;
            string playlistFile;
            var currentDir = Directory.GetCurrentDirectory();
            foreach (var radio in _radios)
            {
                if (radio.StreamList.Count <= 0) continue;
                radiocount++;
                result.AppendLine(
                    $"#EXTINF:-1 group-title=\"Play.cz\" radio=\"true\" tvg-logo=\"{radio.LogoUrl}\" tvg-chno=\"{radiocount}\",{radio.Title}");
                var stream = radio.StreamList[0];
                switch (playlistType)
                {
                    case 1:
                    {
                        // tvheadend
                        var r =
                            $"pipe://ffmpeg -loglevel fatal -i {radio.StreamList[0]} -vn -acodec copy -flags +global_header -strict -2 -metadata service_provider={radio.StreamList[0]} -metadata service_name={radio.Shortcut} -f mpegts -mpegts_service_type digital_radio pipe:1";
                        if (stream.ToLowerInvariant().Contains("mms:"))
                            r = r.Replace("-acodec copy", "-acodec aac"); // prekodovat wma stream
                        result.AppendLine(r);
                        break;
                    }
                    case 2:
                        // hub
                        playlistFile = Path.Combine(currentDir, radio.Shortcut + ".m3u8");
                        File.WriteAllText(playlistFile, string.Join(Environment.NewLine, radio.StreamList.ToArray()));
                        result.AppendLine(playlistFile);
                        break;
                    default:
                        // typ 0
                        result.AppendLine(radio.StreamList[0]);
                        break;
                }
            }

            switch (playlistType)
            {
                case 1:
                    playlistFile = "play.cz.tvheadend.m3u8";
                    break;
                case 2:
                    playlistFile = "play.cz.hub.m3u8";
                    break;
                default:
                    playlistFile = "play.cz.generic.m3u8";
                    break;
            }

            Console.WriteLine(Path.Combine(currentDir, playlistFile));
            File.WriteAllText(Path.Combine(currentDir, playlistFile), result.ToString());
        }

        private static string DownloadToString(string url)
        {
            var httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent",
                "Mozilla/5.0 (Windows; U; Windows NT 5.1; en-GB; rv:1.9.0.3) Gecko/2008092417 Firefox/3.0.3");
            var xr = httpClient.GetStringAsync(url);
            var httpdata = xr.Result;
            httpClient.Dispose();
            return httpdata;
        }
        
        public static string ToWindows1250(this string source)
        {
            var windows1250 = Encoding.ASCII;
            var utf8 = Encoding.UTF8;
            var utfBytes = utf8.GetBytes(source);
            var win1250Bytes = Encoding.Convert(utf8, windows1250, utfBytes);
            return windows1250.GetString(win1250Bytes);
        }
    }
}