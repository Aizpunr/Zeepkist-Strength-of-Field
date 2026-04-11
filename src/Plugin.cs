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
    [BepInPlugin("com.aizpun.strengthoffield", "Strength of Field", "0.2.0")]
    [BepInDependency("ZeepSDK")]
    public class Plugin : BaseUnityPlugin
    {
        // Steam ID -> raw ELO (COTD weighted or GTR-regression-predicted).
        // No normalization — values are on the same raw scale as alldata.json's
        // weighted variant, because the GTR regression was fit in that scale.
        private Dictionary<ulong, double> eloBySteamId = new Dictionary<ulong, double>();
        private double sofDivisor = 2000.0;
        private double minPoolRating = 0;
        private bool dataLoaded = false;
        private bool dataLoading = false;

        private const string ELO_POOL_URL =
            "https://raw.githubusercontent.com/Aizpunr/Zeepkist-Strength-of-Field/main/elo_pool.json";

        private void Awake()
        {
            Logger.LogInfo("Strength of Field plugin loaded!");

            // /sof = local only, detailed output
            ChatCommandApi.RegisterLocalChatCommand("/", "sof", "Show lobby Strength of Field (detailed)",
                (LocalChatCommandCallbackDelegate)OnSofLocal);

            // !sof = fires on anyone's chat message, broadcasts the result
            ChatCommandApi.RegisterMixedChatCommand("!", "sof", "Show lobby SOF",
                (MixedChatCommandCallbackDelegate)OnSofMixed);

            LoadEloData();
        }

        private double CalcSof(out int found, out int notFound, out int total)
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
                if (eloBySteamId.TryGetValue(player.SteamID, out rating))
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
                top10.Add(minPoolRating);

            double avg = top10.Average();
            return Math.Round(avg / sofDivisor * 100.0, 1);
        }

        private void OnSofMixed(bool isLocal, ulong steamId, string arguments)
        {
            // Responds when anyone in the lobby types !sof.
            if (!dataLoaded) return;

            try
            {
                int found, notFound, total;
                double sof = CalcSof(out found, out notFound, out total);
                if (sof < 0) return;

                ChatApi.SendMessage(string.Format("SOF {0} ({1} unrated)", sof, notFound));
            }
            catch (Exception ex)
            {
                Logger.LogError(string.Format("SOF mixed error: {0}", ex));
            }
        }

        private void OnSofLocal(string arguments)
        {
            if (!dataLoaded)
            {
                if (dataLoading)
                    ChatApi.AddLocalMessage("SOF: Loading ELO data, try again in a few seconds...");
                else
                {
                    ChatApi.AddLocalMessage("SOF: ELO data not loaded. Retrying...");
                    LoadEloData();
                }
                return;
            }

            try
            {
                int found, notFound, total;
                double sof = CalcSof(out found, out notFound, out total);

                if (sof < 0)
                {
                    ChatApi.AddLocalMessage(string.Format("SOF: No rated players found ({0} total)", total));
                    return;
                }

                ChatApi.AddLocalMessage(string.Format("SOF: {0} ({1} rated, {2} unrated, {3} total) [v0.2.0]",
                    sof, found, notFound, total));
            }
            catch (Exception ex)
            {
                ChatApi.AddLocalMessage(string.Format("SOF Error: {0}", ex.Message));
                Logger.LogError(string.Format("SOF command error: {0}", ex));
            }
        }

        private void LoadEloData()
        {
            dataLoading = true;
            try
            {
                WebClient client = new WebClient();
                client.DownloadStringCompleted += delegate(object sender, DownloadStringCompletedEventArgs e)
                {
                    try
                    {
                        if (e.Error != null)
                        {
                            Logger.LogError(string.Format("Failed to download ELO data: {0}", e.Error.Message));
                            dataLoading = false;
                            return;
                        }

                        ParseEloData(e.Result);
                        dataLoaded = true;
                        dataLoading = false;
                        Logger.LogInfo(string.Format(
                            "ELO data loaded: {0} players (divisor={1})",
                            eloBySteamId.Count, sofDivisor));
                    }
                    catch (Exception ex)
                    {
                        Logger.LogError(string.Format("Failed to parse ELO data: {0}", ex));
                        dataLoading = false;
                    }
                };
                client.DownloadStringAsync(new Uri(ELO_POOL_URL));
            }
            catch (Exception ex)
            {
                Logger.LogError(string.Format("Failed to start ELO download: {0}", ex));
                dataLoading = false;
            }
        }

        private void ParseEloData(string json)
        {
            JObject root = JObject.Parse(json);

            // Divisor from file; fall back to 2000 if missing.
            JToken divisorToken = root["sof_divisor"];
            if (divisorToken != null)
            {
                sofDivisor = (double)divisorToken;
                if (sofDivisor <= 0) sofDivisor = 2000.0;
            }

            JArray players = (JArray)root["players"];
            if (players == null) return;

            eloBySteamId.Clear();
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

                eloBySteamId[sid] = elo;
            }

            if (eloBySteamId.Count > 0)
                minPoolRating = eloBySteamId.Values.Min();
            else
                minPoolRating = 0;
        }
    }
}
