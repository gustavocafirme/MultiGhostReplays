using MelonLoader;
using Steamworks;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace MultiGhostReplays
{
    /// <summary>
    /// DTO container holding deserialized Steam leaderboard entry details, selection flags, and UGC handles.
    /// </summary>
    public class LeaderboardEntryData
    {
        public int Rank;
        public string PlayerName;
        public int CountryId;
        public int CarId;
        public string CarName;
        public int Score;
        public string FormattedTime;
        public int EntryIndex;
        public bool IsSelected;
        public bool IsDownloading;
        public UGCHandle_t UgcHandle;
    }

    /// <summary>
    /// Handles asynchronous querying of Steamworks leaderboard entries, UGC replay downloads, and state resets.
    /// </summary>
    public static class ReplayDownloader
    {
        public const int MAX_ALLOWED_GHOSTS = 512;
        /// <summary>
        /// Indicates if a batch sequence download coroutine is currently executing.
        /// </summary>
        public static bool isDownloadingBatch = false;

        /// <summary>
        /// Active collection of fetched leaderboard entries for display in the custom UI browser.
        /// </summary>
        public static List<LeaderboardEntryData> loadedEntries = new List<LeaderboardEntryData>();

        /// <summary>
        /// Flag preventing concurrent leaderboard API request calls.
        /// </summary>
        public static bool isFetchingEntries = false;

        private static CallResult<LeaderboardScoresDownloaded_t> onScoresDownloadedCallResult;

        /// <summary>
        /// Maps native vehicle ID integers to readable vehicle display names.
        /// </summary>
        private static readonly string[] CarNamesMap = new string[]
        {
            "Rookie", "Shorty", "Baron", "Fox", "Petite", "Beast", "Dust Devil", "Thunderbolt",
            "Stylo", "Phantom", "Turbo", "Champion", "Fury", "La Princess", "Flow", "Contender",
            "Sandworm", "Classic", "Storm", "Kaiser", "Super", "Falcon", "Rocket", "Hurricane",
            "Crusher", "Nitro", "Phoenix", "Typhoon", "El Toro", "Roadslayer", "Dakar", "Chaser",
            "Big Pride", "Fire Fighter", "Soap Spinner", "Godspeed", "Crawler FWD", "Hellcat",
            "Panther", "Star Kart", "Mr. President", "Antivirus", "Oldschool", "Gorilla", "Horizon",
            "The Rock", "Colossus", "Majesty", "Delirium", "Safari", "Unrivaled RWD", "Hallowheel"
        };

        /// <summary>
        /// Initiates an asynchronous Steam API call to download leaderboard entries within the specified rank range.
        /// </summary>
        /// <param name="rangeStart">1-based starting global rank index.</param>
        /// <param name="rangeEnd">1-based ending global rank index.</param>
        public static void FetchLeaderboardRange(int rangeStart, int rangeEnd)
        {
            if (isFetchingEntries) return;

            var steamFuncs = GameManager.game_manager?.steam_functions;
            if (steamFuncs == null)
            {
                MelonLogger.Warning("[Mod] SteamFunctions instance is null.");
                return;
            }

            FieldInfo handleField = typeof(SteamFunctions).GetField("currentlySelectedLeaderboard", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (handleField == null) return;

            SteamLeaderboard_t handle = (SteamLeaderboard_t)handleField.GetValue(steamFuncs);
            if (handle.m_SteamLeaderboard == 0)
            {
                steamFuncs.RequestWorldRecordLeaderboard(TrackManager.current_track_id, false);
                return;
            }

            isFetchingEntries = true;
            MainMod.statusMessage = $"Fetching #{rangeStart}..#{rangeEnd}...";

            SteamAPICall_t call = SteamUserStats.DownloadLeaderboardEntries(handle, ELeaderboardDataRequest.k_ELeaderboardDataRequestGlobal, rangeStart, rangeEnd);

            if (onScoresDownloadedCallResult == null)
            {
                onScoresDownloadedCallResult = CallResult<LeaderboardScoresDownloaded_t>.Create(OnScoresDownloaded);
            }

            onScoresDownloadedCallResult.Set(call);
        }

        /// <summary>
        /// Callback handler triggered when Steam returns downloaded leaderboard entries.
        /// Parses entry metadata and populates the local <see cref="loadedEntries"/> list.
        /// </summary>
        private static void OnScoresDownloaded(LeaderboardScoresDownloaded_t pCallback, bool bIOFailure)
        {
            isFetchingEntries = false;

            if (bIOFailure || pCallback.m_cEntryCount == 0)
            {
                MainMod.statusMessage = bIOFailure ? "Steam IO Error" : "No entries found";
                return;
            }

            int count = pCallback.m_cEntryCount;
            SteamLeaderboardEntries_t hEntries = pCallback.m_hSteamLeaderboardEntries;

            for (int i = 0; i < count; i++)
            {
                int[] details = new int[3];
                LeaderboardEntry_t entry;

                if (SteamUserStats.GetDownloadedLeaderboardEntry(hEntries, i, out entry, details, details.Length))
                {
                    int carType = details[0];
                    int countryId = details[2];
                    float timeSeconds = (float)entry.m_nScore / 1000f;

                    LeaderboardEntryData data = new LeaderboardEntryData
                    {
                        Rank = entry.m_nGlobalRank,
                        PlayerName = SteamFriends.GetFriendPersonaName(entry.m_steamIDUser),
                        CountryId = countryId,
                        CarId = carType,
                        CarName = GetCarNameByID(carType),
                        Score = entry.m_nScore,
                        FormattedTime = NotificationManager.FormatTime(timeSeconds),
                        EntryIndex = loadedEntries.Count,
                        IsSelected = false,
                        IsDownloading = false,
                        UgcHandle = entry.m_hUGC
                    };

                    loadedEntries.Add(data);
                }
            }

            MainMod.statusMessage = "Ready";
        }

        /// <summary>
        /// Converts integer vehicle type IDs to human-readable vehicle strings.
        /// </summary>
        private static string GetCarNameByID(int carType)
        {
            if (carType >= 0 && carType < CarNamesMap.Length)
            {
                return CarNamesMap[carType];
            }
            return $"Car #{carType}";
        }

        /// <summary>
        /// Coroutine that sequentially downloads UGC replay files for all selected entries
        /// Applies a silent limit of 512 maximum ghosts to prevent GPU/CPU crashes
        /// </summary>
        /// <param name="steamFuncs">Active instance of SteamFunctions used to invoke UGC downloads.</param>
        public static IEnumerator DownloadAndLaunchSelectedEntries(SteamFunctions steamFuncs)
        {
            isDownloadingBatch = true;
            MainMod.queuedLeaderboardReplays.Clear();
            int trackID = TrackManager.current_track_id;
            int loadedCount = 0;

            FieldInfo handleField = typeof(SteamFunctions).GetField("currentlySelectedLeaderboard", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            SteamLeaderboard_t leaderboardHandle = handleField != null ? (SteamLeaderboard_t)handleField.GetValue(steamFuncs) : default;

            for (int i = 0; i < loadedEntries.Count; i++)
            {
                // Silent lock: interrupts the loop when it reaches the limit of 512 replays
                if (loadedCount >= MAX_ALLOWED_GHOSTS)
                {
                    MelonLogger.Msg($"[Mod] Maximum ghosts reached ({MAX_ALLOWED_GHOSTS} ghosts). Starting replay with the loaded selection.");
                    break;
                }

                var entry = loadedEntries[i];
                if (entry.IsSelected)
                {
                    entry.IsDownloading = true;
                    MainMod.statusMessage = $"Downloading #{entry.Rank} ({loadedCount + 1}/{MAX_ALLOWED_GHOSTS})...";

                    ClearDownloadedUGCBuffer();

                    // Synchronize native entry index pointers to avoid callback mismatch exceptions
                    SteamFunctions.selectedLeaderboardEntry = entry.EntryIndex;
                    SetSelectedEntryDLStarted(steamFuncs, entry.EntryIndex);

                    if (entry.UgcHandle.m_UGCHandle != 0 && leaderboardHandle.m_SteamLeaderboard != 0)
                    {
                        steamFuncs.DownloadUGC(entry.UgcHandle, leaderboardHandle, entry.EntryIndex);
                    }
                    else
                    {
                        steamFuncs.DownloadUGCForSelectedLeaderboardEntry();
                    }

                    // Await UGC download completion with a 4.0 second fallback timeout
                    float timeout = 4.0f;
                    ReplayData downloaded = null;

                    while (timeout > 0f)
                    {
                        yield return new WaitForSeconds(0.15f);
                        downloaded = SteamFunctions.GetDownloadedUGCReplay(trackID);
                        if (downloaded != null) break;
                        timeout -= 0.15f;
                    }

                    entry.IsDownloading = false;

                    if (downloaded != null)
                    {
                        MainMod.queuedLeaderboardReplays.Add(downloaded);
                        loadedCount++;
                        MelonLogger.Msg($"[Mod] Extended Replay #{entry.Rank} ({downloaded.playername}) loaded.");
                    }
                    else
                    {
                        MelonLogger.Warning($"[Mod] Replay #{entry.Rank} download timed out.");
                    }
                }
            }

            if (MainMod.queuedLeaderboardReplays.Count > 0)
            {
                if (loadedCount >= MAX_ALLOWED_GHOSTS)
                {
                    MainMod.statusMessage = $"{MainMod.queuedLeaderboardReplays.Count} Replays Ready! (Max Limit {MAX_ALLOWED_GHOSTS})";
                }
                else
                {
                    MainMod.statusMessage = $"{MainMod.queuedLeaderboardReplays.Count} Replays Ready!";
                }

                isDownloadingBatch = false;
                // Command native GameManager to transition to replay mode (Menu state 5)
                GameManager.game_manager.MainMenuButtonPressed(5);
            }
            else
            {
                MainMod.statusMessage = "No replays loaded!";
                isDownloadingBatch = false;
            }
        }

        /// <summary>
        /// Sets reflection values for selectedLeaderboardEntryWhenDLStarted on SteamFunctions.
        /// </summary>
        private static void SetSelectedEntryDLStarted(SteamFunctions steamFuncs, int value)
        {
            FieldInfo field = typeof(SteamFunctions).GetField("selectedLeaderboardEntryWhenDLStarted", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance);
            if (field != null)
            {
                if (field.IsStatic)
                    field.SetValue(null, value);
                else
                    field.SetValue(steamFuncs, value);
            }
        }

        /// <summary>
        /// Resets the cached downloaded UGC replay reference in SteamFunctions via reflection.
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
        /// Clears all stored leaderboard entries and resets fetching/downloading state flags.
        /// Called automatically upon track change or manual window resets.
        /// </summary>
        public static void ResetTableData()
        {
            loadedEntries.Clear();
            isFetchingEntries = false;
            isDownloadingBatch = false;
            MainMod.statusMessage = "Ready";
            LeaderboardUI.showExtendedWindow = false;
        }
    }
}