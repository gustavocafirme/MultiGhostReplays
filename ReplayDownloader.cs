using MelonLoader;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace MultiGhostReplays
{
    /// <summary>
    /// Manages UGC replay downloading queues and Steam API integration.
    /// </summary>
    public static class ReplayDownloader
    {
        public static bool isDownloadingBatch = false;

        /// <summary>
        /// Downloads selected leaderboard replays sequentially and launches the replay camera mode once completed.
        /// </summary>
        public static IEnumerator DownloadAndLaunchSelectedReplays(SteamFunctions steamFuncs, IList entries, bool[] selectedEntries)
        {
            isDownloadingBatch = true;
            MainMod.queuedLeaderboardReplays.Clear();

            int trackID = TrackManager.current_track_id;

            for (int i = 0; i < selectedEntries.Length; i++)
            {
                if (selectedEntries[i] && i < entries.Count)
                {
                    MainMod.statusMessage = $"Downloading #{i + 1}...";

                    ClearDownloadedUGCBuffer();

                    SteamFunctions.selectedLeaderboardEntry = i;
                    steamFuncs.DownloadUGCForSelectedLeaderboardEntry();

                    float timeout = 4.0f;
                    ReplayData downloaded = null;

                    while (timeout > 0f)
                    {
                        yield return new WaitForSeconds(0.15f);
                        downloaded = SteamFunctions.GetDownloadedUGCReplay(trackID);

                        if (downloaded != null) break;
                        timeout -= 0.15f;
                    }

                    if (downloaded != null)
                    {
                        MainMod.queuedLeaderboardReplays.Add(downloaded);
                        MelonLogger.Msg($"[Mod] Replay #{i + 1} ({downloaded.playername}) downloaded and queued.");
                    }
                    else
                    {
                        MelonLogger.Warning($"[Mod] Replay #{i + 1} failed or timed out.");
                    }
                }
            }

            if (MainMod.queuedLeaderboardReplays.Count > 0)
            {
                MainMod.statusMessage = $"{MainMod.queuedLeaderboardReplays.Count} Replays Ready!";
                isDownloadingBatch = false;

                GameManager.game_manager.MainMenuButtonPressed(5);
            }
            else
            {
                MainMod.statusMessage = "Failed to download selections!";
                isDownloadingBatch = false;
            }
        }

        /// <summary>
        /// Clears the static downloaded UGC buffer in SteamFunctions via reflection before issuing new requests.
        /// </summary>
        public static void ClearDownloadedUGCBuffer()
        {
            FieldInfo field = typeof(SteamFunctions).GetField("downloadedUGCReplay", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance);
            if (field != null)
            {
                field.SetValue(null, null);
            }
        }

        /// <summary>
        /// Retrieves the active leaderboard entry list from SteamFunctions using reflection.
        /// </summary>
        public static IList GetLeaderboardEntriesList(SteamFunctions steamFuncs)
        {
            FieldInfo field = typeof(SteamFunctions).GetField("leaderboardEntries", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            return field != null ? field.GetValue(steamFuncs) as IList : null;
        }
    }
}