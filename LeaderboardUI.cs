using MelonLoader;
using System;
using UnityEngine;

namespace MultiGhostReplays
{
    /// <summary>
    /// Handles the OnGUI rendering and user interactions for the custom Leaderboard Replay Browser window.
    /// Provides controls for range selection, downloading, and launching ghost replays.
    /// </summary>
    public static class LeaderboardUI
    {
        /// <summary>
        /// Controls the visibility state of the extended modal leaderboard replay browser.
        /// </summary>
        public static bool showExtendedWindow = false;

        private static Vector2 scrollPosition = Vector2.zero;

        /// <summary>
        /// Draws the primary entry button and triggers the rendering of the extended leaderboard window when active.
        /// </summary>
        public static void DrawLeaderboardCheckboxes()
        {
            var steamFuncs = GameManager.game_manager?.steam_functions;
            if (steamFuncs == null) return;

            // Compute resolution scale factors relative to baseline 1080p
            float scaleX = Screen.width / 1920f;
            float scaleY = Screen.height / 1080f;

            float btnWidth = (float)Math.Round(140f * scaleX);
            float btnHeight = (float)Math.Round(45f * scaleY);
            float btnX = Screen.width - (float)Math.Round(465f * scaleX) - btnWidth;
            float btnY = Screen.height - (float)Math.Round(445f * scaleY);

            GUIStyle fullTableBtnStyle = new GUIStyle(GUI.skin.button)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = (int)Math.Round(14f * scaleX),
                fontStyle = FontStyle.Bold
            };

            // Main entry point button to toggle the full leaderboard modal
            if (GUI.Button(new Rect(btnX, btnY, btnWidth, btnHeight), "⊞ LEADERBOARD", fullTableBtnStyle))
            {
                showExtendedWindow = !showExtendedWindow;

                // Automatically fetch initial batch (ranks 1-50) if opening an empty table
                if (showExtendedWindow && ReplayDownloader.loadedEntries.Count == 0)
                {
                    ReplayDownloader.FetchLeaderboardRange(1, 50);
                }
            }

            if (showExtendedWindow)
            {
                DrawExtendedLeaderboardWindow(steamFuncs);
            }
        }

        /// <summary>
        /// Renders the modal window containing the scrollable leaderboard entry table and batch action buttons.
        /// </summary>
        /// <param name="steamFuncs">Active instance of the native SteamFunctions manager.</param>
        private static void DrawExtendedLeaderboardWindow(SteamFunctions steamFuncs)
        {
            float winWidth = 610f;
            float winHeight = Screen.height / 2;
            float winX = (Screen.width - winWidth) / 2f;
            float winY = (Screen.height - winHeight) / 2f;

            // Modal window background and header frame
            GUI.Box(new Rect(winX, winY, winWidth, winHeight), "");
            GUI.Box(new Rect(winX + 2, winY + 2, winWidth - 4, winHeight - 4), "Leaderboard Replay Browser");

            // Close button
            if (GUI.Button(new Rect(winX + winWidth - 30f, winY + 5f, 24f, 22f), "X"))
            {
                showExtendedWindow = false;
            }

            // Select all loaded entries
            if (GUI.Button(new Rect(winX + 15f, winY + 30f, 30f, 24f), "✓"))
            {
                foreach (var entry in ReplayDownloader.loadedEntries) entry.IsSelected = true;
            }

            // Deselect all loaded entries
            if (GUI.Button(new Rect(winX + 50f, winY + 30f, 30f, 24f), "X"))
            {
                foreach (var entry in ReplayDownloader.loadedEntries) entry.IsSelected = false;
            }

            // Trigger batch download and game scene setup for selected replays
            int selectedCount = ReplayDownloader.loadedEntries.FindAll(e => e.IsSelected).Count;
            string btnText = selectedCount > 0 ? $"LOAD ({Math.Min(selectedCount, ReplayDownloader.MAX_ALLOWED_GHOSTS)})" : "LOAD SELECTED";

            if (!ReplayDownloader.isDownloadingBatch && GUI.Button(new Rect(winX + 90f, winY + 30f, 130f, 24f), btnText))
            {
                MelonCoroutines.Start(ReplayDownloader.DownloadAndLaunchSelectedEntries(steamFuncs));
            }

            // Table Column Headers
            float headerY = winY + 62f;
            GUI.Box(new Rect(winX + 10f, headerY, winWidth - 35f, 26f), "");
            GUI.Label(new Rect(winX + 15f, headerY + 3f, 35f, 20f), "Set");
            GUI.Label(new Rect(winX + 55f, headerY + 3f, 25f, 20f), "Chk");
            GUI.Label(new Rect(winX + 85f, headerY + 3f, 45f, 20f), "Rank");
            GUI.Label(new Rect(winX + 135f, headerY + 3f, 40f, 20f), "Flag");
            GUI.Label(new Rect(winX + 180f, headerY + 3f, 180f, 20f), "Player Name");
            GUI.Label(new Rect(winX + 370f, headerY + 3f, 120f, 20f), "Car Model");
            GUI.Label(new Rect(winX + 500f, headerY + 3f, 120f, 20f), "Time");

            // Scrollable Entry Table Area
            float tableY = winY + 92f;
            float tableHeight = winHeight - 145f;
            float contentHeight = ReplayDownloader.loadedEntries.Count * 28f;

            scrollPosition = GUI.BeginScrollView(
                new Rect(winX + 10f, tableY, winWidth - 20f, tableHeight),
                scrollPosition,
                new Rect(0f, 0f, winWidth - 40f, contentHeight)
            );

            Color defaultGuiColor = GUI.color;

            // Custom button style with zero padding to prevent text overflow artifacts on range toggles
            GUIStyle rangeButtonStyle = new GUIStyle(GUI.skin.button)
            {
                alignment = TextAnchor.MiddleCenter,
                padding = new RectOffset(0, 0, 0, 0)
            };

            for (int i = 0; i < ReplayDownloader.loadedEntries.Count; i++)
            {
                var entry = ReplayDownloader.loadedEntries[i];
                float rowY = i * 28f;

                // Highlight actively downloading entry rows in red
                if (entry.IsDownloading)
                {
                    GUI.color = Color.red;
                    GUI.Box(new Rect(0f, rowY, 675f, 26f), "");
                    GUI.color = defaultGuiColor;
                }

                // Range toggle button (selects/deselects all rows up to index i)
                if (GUI.Button(new Rect(5f, rowY + 2f, 32f, 22f), "", rangeButtonStyle))
                {
                    ToggleRangeSelectionUpTo(i);
                }

                // Individual row selection toggle
                entry.IsSelected = GUI.Toggle(new Rect(45f, rowY + 4f, 20f, 20f), entry.IsSelected, "");

                // Entry Metadata Labels
                GUI.Label(new Rect(75f, rowY + 3f, 45f, 20f), $"#{entry.Rank}");

                // Country flag texture rendering
                if (entry.CountryId >= 0 && GameManager.game_manager != null && entry.CountryId < GameManager.game_manager.countryFlags.Length)
                {
                    Texture flagTex = (Texture)GameManager.game_manager.countryFlags[entry.CountryId];
                    if (flagTex != null)
                    {
                        GUI.DrawTexture(new Rect(125f, rowY + 4f, 24f, 16f), flagTex);
                    }
                }

                GUI.Label(new Rect(170f, rowY + 3f, 180f, 20f), entry.PlayerName);
                GUI.Label(new Rect(360f, rowY + 3f, 120f, 20f), entry.CarName);
                GUI.Label(new Rect(490f, rowY + 3f, 120f, 20f), entry.FormattedTime);
            }

            GUI.EndScrollView();

            // Pagination button to dynamically fetch 50 additional entries
            float bottomY = winY + winHeight - 42f;
            if (!ReplayDownloader.isFetchingEntries && GUI.Button(new Rect(winX + (winWidth - 160f) / 2f, bottomY, 160f, 30f), "Add 50 Players"))
            {
                int nextStart = ReplayDownloader.loadedEntries.Count + 1;
                ReplayDownloader.FetchLeaderboardRange(nextStart, nextStart + 49);
            }
        }

        /// <summary>
        /// Toggles selection state for all leaderboard entries from index 0 up to targetIndex.
        /// If all entries in range are checked, deselects them all; otherwise, selects all in range.
        /// </summary>
        /// <param name="targetIndex">The end index of the range operation.</param>
        private static void ToggleRangeSelectionUpTo(int targetIndex)
        {
            bool allChecked = true;

            for (int i = 0; i <= targetIndex; i++)
            {
                if (i < ReplayDownloader.loadedEntries.Count && !ReplayDownloader.loadedEntries[i].IsSelected)
                {
                    allChecked = false;
                    break;
                }
            }

            bool newState = !allChecked;

            for (int i = 0; i <= targetIndex; i++)
            {
                if (i < ReplayDownloader.loadedEntries.Count)
                {
                    ReplayDownloader.loadedEntries[i].IsSelected = newState;
                }
            }
        }
    }
}