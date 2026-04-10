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
    [BepInPlugin("com.aizpun.strengthoffield", "Strength of Field", "0.1.1")]
    [BepInDependency("ZeepSDK")]
    public class Plugin : BaseUnityPlugin
    {
        private Dictionary<string, double> normalizedPool = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        private double minPoolRating = 0;
        private bool dataLoaded = false;
        private bool dataLoading = false;

        private void Awake()
        {
            Logger.LogInfo("Strength of Field plugin loaded!");

            // /sof = local only, detailed output
            ChatCommandApi.RegisterLocalChatCommand("/", "sof", "Show lobby Strength of Field (detailed)",
                (LocalChatCommandCallbackDelegate)OnSofLocal);

            // !sof = works for anyone in chat, short output
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
                string name = player.GetUserNameNoTag();
                if (string.IsNullOrEmpty(name)) continue;

                double rating;
                if (normalizedPool.TryGetValue(name, out rating))
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
            return Math.Round(avg / 1850.0 * 100.0, 1);
        }

        private string GetPlayerName(ulong steamId)
        {
            var players = ZeepkistNetwork.PlayerList;
            if (players == null) return null;
            foreach (var p in players)
            {
                if (p.SteamID == steamId)
                    return p.GetUserNameNoTag();
            }
            return null;
        }

        private void OnSofMixed(bool isLocal, ulong steamId, string arguments)
        {
            // Respond when anyone in the lobby types !sof.
            // NOTE: if multiple players have the mod, you'll get duplicate broadcasts.
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

                ChatApi.AddLocalMessage(string.Format("SOF: {0} ({1} rated, {2} unrated, {3} total) [v0.1.1]",
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
                        Logger.LogInfo(string.Format("ELO data loaded: {0} players", normalizedPool.Count));
                    }
                    catch (Exception ex)
                    {
                        Logger.LogError(string.Format("Failed to parse ELO data: {0}", ex));
                        dataLoading = false;
                    }
                };
                client.DownloadStringAsync(new Uri(
                    "https://raw.githubusercontent.com/Aizpunr/Zeepkist-COTD-Elo-Rankings/main/alldata.json"
                ));
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
            JArray weighted = (JArray)root["weighted"];

            Dictionary<string, double> ratings = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

            foreach (JObject player in weighted)
            {
                string name = (string)player["n"];
                double rating = (double)player["r"];
                if (name != null && rating > 0)
                {
                    ratings[name] = rating;
                }
            }

            if (ratings.Count == 0) return;

            double maxRating = ratings.Values.Max();
            double scale = 2000.0 / maxRating;

            normalizedPool.Clear();
            foreach (var kvp in ratings)
            {
                normalizedPool[kvp.Key] = Math.Round(kvp.Value * scale, 1);
            }

            minPoolRating = normalizedPool.Values.Min();
        }
    }
}
