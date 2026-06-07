using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using BepInEx;
using Newtonsoft.Json.Linq;
using ZeepSDK.Chat;
using ZeepSDK.ChatCommands;
using ZeepkistClient;

namespace StrengthOfField
{
    [BepInPlugin("com.aizpun.strengthoffield", "Strength of Field", "1.1.0")]
    [BepInDependency("ZeepSDK")]
    public class Plugin : BaseUnityPlugin
    {
        // A rating pool: Steam ID -> rating, with its own divisor and label.
        // Two pools are loaded:
        //   crosscomp — cross-comp ELO across all 5 comps. Recency-biased:
        //               answers "how hard is this lobby right now". (!sof / /sof)
        //   cotd      — COTD-only weighted ELO. Historical cup pedigree.
        //               (!sof cup / /sof cup)
        // Values are raw ELO on each pool's own scale; SOF = avg(top10) / divisor.
        private class Pool
        {
            public readonly string Url;
            public readonly string Label;   // shown in output, e.g. "SOF" or "Cup Strength"
            public Dictionary<ulong, double> Elo = new Dictionary<ulong, double>();
            public double Divisor = 2000.0;
            public double MinRating = 0;
            public bool Loaded = false;
            public bool Loading = false;

            public Pool(string url, string label)
            {
                Url = url;
                Label = label;
            }
        }

        private const string CROSSCOMP_URL =
            "https://raw.githubusercontent.com/Aizpunr/Zeepkist-Strength-of-Field/main/elo_pool.json";
        private const string COTD_URL =
            "https://raw.githubusercontent.com/Aizpunr/Zeepkist-Strength-of-Field/main/elo_pool_cotd.json";

        private Pool crosscomp;
        private Pool cotd;

        private void Awake()
        {
            Logger.LogInfo("Strength of Field plugin loaded!");

            crosscomp = new Pool(CROSSCOMP_URL, "SOF");
            cotd = new Pool(COTD_URL, "Cup Strength");

            // /sof = local only, detailed output. "/sof cup" reads the COTD pool.
            ChatCommandApi.RegisterLocalChatCommand("/", "sof",
                "Show lobby Strength of Field (add 'cup' for historical COTD strength)",
                (LocalChatCommandCallbackDelegate)OnSofLocal);

            // !sof = fires on anyone's chat message, broadcasts the result.
            // "!sof cup" broadcasts COTD-based cup strength instead.
            ChatCommandApi.RegisterMixedChatCommand("!", "sof",
                "Show lobby SOF (add 'cup' for historical COTD strength)",
                (MixedChatCommandCallbackDelegate)OnSofMixed);

            LoadPool(crosscomp);
            LoadPool(cotd);
        }

        // True when the argument selects the COTD ("cup") pool. Exact "cup"
        // (case-insensitive, trimmed); anything else falls back to cross-comp.
        private bool WantsCup(string arguments)
        {
            if (string.IsNullOrEmpty(arguments)) return false;
            return arguments.Trim().Equals("cup", StringComparison.OrdinalIgnoreCase);
        }

        private double CalcSof(Pool pool, out int found, out int notFound, out int total)
        {
            found = 0;
            notFound = 0;
            total = 0;

            var players = ZeepkistNetwork.PlayerList;
            if (players == null || players.Count == 0) return -1;

            total = players.Count;
            List<double> elos = new List<double>();

            foreach (var player in players)
            {
                double rating;
                if (pool.Elo.TryGetValue(player.SteamID, out rating))
                {
                    elos.Add(rating);
                    found++;
                }
                else
                {
                    notFound++;
                }
            }

            List<double> top10 = elos.OrderByDescending(delegate(double e) { return e; }).Take(10).ToList();
            if (top10.Count == 0) return -1;

            while (top10.Count < 10)
                top10.Add(pool.MinRating);

            double avg = top10.Average();
            return Math.Round(avg / pool.Divisor * 100.0, 1);
        }

        private void OnSofMixed(bool isLocal, ulong steamId, string arguments)
        {
            // Only the typer's own client broadcasts, so multiple installs don't dupe.
            if (!isLocal) return;

            Pool pool = WantsCup(arguments) ? cotd : crosscomp;
            if (!pool.Loaded) return;

            try
            {
                int found, notFound, total;
                double sof = CalcSof(pool, out found, out notFound, out total);
                if (sof < 0) return;

                ChatApi.SendMessage(string.Format("{0} {1} ({2} unrated)", pool.Label, sof, notFound));
            }
            catch (Exception ex)
            {
                Logger.LogError(string.Format("SOF mixed error: {0}", ex));
            }
        }

        private void OnSofLocal(string arguments)
        {
            Pool pool = WantsCup(arguments) ? cotd : crosscomp;

            if (!pool.Loaded)
            {
                if (pool.Loading)
                    ChatApi.AddLocalMessage(string.Format(
                        "{0}: Loading ELO data, try again in a few seconds...", pool.Label));
                else
                {
                    ChatApi.AddLocalMessage(string.Format(
                        "{0}: ELO data not loaded. Retrying...", pool.Label));
                    LoadPool(pool);
                }
                return;
            }

            try
            {
                int found, notFound, total;
                double sof = CalcSof(pool, out found, out notFound, out total);

                if (sof < 0)
                {
                    ChatApi.AddLocalMessage(string.Format("{0}: No rated players found ({1} total)", pool.Label, total));
                    return;
                }

                ChatApi.AddLocalMessage(string.Format("{0}: {1} ({2} rated, {3} unrated, {4} total)",
                    pool.Label, sof, found, notFound, total));
            }
            catch (Exception ex)
            {
                ChatApi.AddLocalMessage(string.Format("{0} Error: {1}", pool.Label, ex.Message));
                Logger.LogError(string.Format("SOF command error: {0}", ex));
            }
        }

        private void LoadPool(Pool pool)
        {
            pool.Loading = true;
            try
            {
                WebClient client = new WebClient();
                client.DownloadStringCompleted += delegate(object sender, DownloadStringCompletedEventArgs e)
                {
                    try
                    {
                        if (e.Error != null)
                        {
                            Logger.LogError(string.Format("Failed to download {0} data: {1}", pool.Label, e.Error.Message));
                            pool.Loading = false;
                            return;
                        }

                        ParsePool(pool, e.Result);
                        pool.Loaded = true;
                        pool.Loading = false;
                        Logger.LogInfo(string.Format(
                            "{0} data loaded: {1} players (divisor={2})",
                            pool.Label, pool.Elo.Count, pool.Divisor));
                    }
                    catch (Exception ex)
                    {
                        Logger.LogError(string.Format("Failed to parse {0} data: {1}", pool.Label, ex));
                        pool.Loading = false;
                    }
                };
                client.DownloadStringAsync(new Uri(pool.Url));
            }
            catch (Exception ex)
            {
                Logger.LogError(string.Format("Failed to start {0} download: {1}", pool.Label, ex));
                pool.Loading = false;
            }
        }

        private void ParsePool(Pool pool, string json)
        {
            JObject root = JObject.Parse(json);

            // Divisor from file; fall back to 2000 if missing.
            JToken divisorToken = root["sof_divisor"];
            if (divisorToken != null)
            {
                pool.Divisor = (double)divisorToken;
                if (pool.Divisor <= 0) pool.Divisor = 2000.0;
            }

            JArray players = (JArray)root["players"];
            if (players == null) return;

            pool.Elo.Clear();
            foreach (JObject player in players)
            {
                string sidStr = (string)player["steam_id"];
                if (string.IsNullOrEmpty(sidStr)) continue;

                ulong sid;
                if (!ulong.TryParse(sidStr, out sid)) continue;

                JToken eloTok = player["elo"];
                if (eloTok == null) continue;
                double elo = (double)eloTok;
                if (elo <= 0) continue;

                pool.Elo[sid] = elo;
            }

            if (pool.Elo.Count > 0)
                pool.MinRating = pool.Elo.Values.Min();
            else
                pool.MinRating = 0;
        }
    }
}
