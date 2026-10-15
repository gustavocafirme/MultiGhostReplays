using MelonLoader;
using System;
using UnityEngine;

namespace MultiGhostReplays
{
    public static class LeaderboardUI
    {
        public static bool showExtendedWindow = false;
        private static Vector2 scrollPosition = Vector2.zero;

        public static void DrawLeaderboardCheckboxes()
        {
            var steamFuncs = GameManager.game_manager?.steam_functions;
            if (steamFuncs == null) return;

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

            if (GUI.Button(new Rect(btnX, btnY, btnWidth, btnHeight), "⊞ LEADERBOARD", fullTableBtnStyle))
            {
                showExtendedWindow = !showExtendedWindow;

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

        private static void DrawExtendedLeaderboardWindow(SteamFunctions steamFuncs)
        {
            float winWidth = 610f;
            float winHeight = Screen.height / 2f;
            float winX = (Screen.width - winWidth) / 2f;
            float winY = (Screen.height - winHeight) / 2f;

            // Window frames
            GUI.Box(new Rect(winX, winY, winWidth, winHeight), "");
            GUI.Box(new Rect(winX + 2, winY + 2, winWidth - 4, winHeight - 4), "Leaderboard Replay Browser");

            // Close button
            if (GUI.Button(new Rect(winX + winWidth - 30f, winY + 5f, 24f, 22f), "X"))
            {
                showExtendedWindow = false;
            }

            // Audio overflow warning (> 166 selected)
            int selectedCount = ReplayDownloader.loadedEntries.FindAll(e => e.IsSelected).Count;
            bool shouldShowTooltip = false;

            if (selectedCount > 166)
            {
                GUIStyle warningStyle = new GUIStyle(GUI.skin.label)
                {
                    alignment = TextAnchor.MiddleRight,
                    fontSize = 11,
                    fontStyle = FontStyle.Bold
                };
                warningStyle.normal.textColor = Color.red;

                Rect warningRect = new Rect(winX + winWidth - 170f, winY + 32f, 160f, 24f);
                GUI.Label(warningRect, "⚠️ AUDIO OVERFLOW", warningStyle);

                if (warningRect.Contains(Event.current.mousePosition))
                {
                    shouldShowTooltip = true;
                }
            }

            // Selection controls
            if (GUI.Button(new Rect(winX + 15f, winY + 30f, 30f, 24f), "✓"))
            {
                foreach (var entry in ReplayDownloader.loadedEntries) entry.IsSelected = true;
            }

            if (GUI.Button(new Rect(winX + 50f, winY + 30f, 30f, 24f), "X"))
            {
                foreach (var entry in ReplayDownloader.loadedEntries) entry.IsSelected = false;
            }

            // Action Buttons
            if (!ReplayDownloader.isDownloadingBatch)
            {
                if (GUI.Button(new Rect(winX + 90f, winY + 30f, 180f, 24f), "VIEW WITH SELECTED"))
                {
                    MelonCoroutines.Start(ReplayDownloader.ViewReplayWithSelectedEntries(steamFuncs));
                }

                if (GUI.Button(new Rect(winX + 280f, winY + 30f, 120f, 24f), "LOAD SELECTED"))
                {
                    MelonCoroutines.Start(ReplayDownloader.LoadSelectedEntriesOnly(steamFuncs));
                }
            }

            // Headers
            float headerY = winY + 62f;
            GUI.Box(new Rect(winX + 10f, headerY, winWidth - 35f, 26f), "");
            GUI.Label(new Rect(winX + 15f, headerY + 3f, 35f, 20f), "Set");
            GUI.Label(new Rect(winX + 55f, headerY + 3f, 25f, 20f), "Chk");
            GUI.Label(new Rect(winX + 85f, headerY + 3f, 45f, 20f), "Rank");
            GUI.Label(new Rect(winX + 135f, headerY + 3f, 40f, 20f), "Flag");
            GUI.Label(new Rect(winX + 180f, headerY + 3f, 180f, 20f), "Player Name");
            GUI.Label(new Rect(winX + 370f, headerY + 3f, 120f, 20f), "Car Model");
            GUI.Label(new Rect(winX + 500f, headerY + 3f, 120f, 20f), "Time");

            // Scroll View Area
            float tableY = winY + 92f;
            float tableHeight = winHeight - 145f;
            float contentHeight = ReplayDownloader.loadedEntries.Count * 28f;

            scrollPosition = GUI.BeginScrollView(
                new Rect(winX + 10f, tableY, winWidth - 20f, tableHeight),
                scrollPosition,
                new Rect(0f, 0f, winWidth - 40f, contentHeight)
            );

            Color defaultGuiColor = GUI.color;

            GUIStyle rangeButtonStyle = new GUIStyle(GUI.skin.button)
            {
                alignment = TextAnchor.MiddleCenter,
                padding = new RectOffset(0, 0, 0, 0)
            };

            GUIStyle labelStyle = new GUIStyle(GUI.skin.label);

            for (int i = 0; i < ReplayDownloader.loadedEntries.Count; i++)
            {
                var entry = ReplayDownloader.loadedEntries[i];
                float rowY = i * 28f;

                if (entry.IsDownloading)
                {
                    GUI.color = Color.red;
                    GUI.Box(new Rect(0f, rowY, winWidth - 40f, 26f), "");
                    GUI.color = defaultGuiColor;
                }

                if (GUI.Button(new Rect(5f, rowY + 2f, 32f, 22f), "", rangeButtonStyle))
                {
                    ToggleRangeSelectionUpTo(i);
                }

                entry.IsSelected = GUI.Toggle(new Rect(45f, rowY + 4f, 20f, 20f), entry.IsSelected, "");

                // Yellow text for cached replays
                labelStyle.normal.textColor = entry.IsCached ? Color.yellow : Color.white;

                GUI.Label(new Rect(75f, rowY + 3f, 45f, 20f), $"#{entry.Rank}", labelStyle);

                if (entry.CountryId >= 0 && GameManager.game_manager != null && entry.CountryId < GameManager.game_manager.countryFlags.Length)
                {
                    Texture flagTex = (Texture)GameManager.game_manager.countryFlags[entry.CountryId];
                    if (flagTex != null)
                    {
                        GUI.DrawTexture(new Rect(125f, rowY + 4f, 24f, 16f), flagTex);
                    }
                }

                GUI.Label(new Rect(170f, rowY + 3f, 180f, 20f), entry.PlayerName, labelStyle);
                GUI.Label(new Rect(360f, rowY + 3f, 120f, 20f), entry.CarName, labelStyle);
                GUI.Label(new Rect(490f, rowY + 3f, 120f, 20f), entry.FormattedTime, labelStyle);
            }

            GUI.EndScrollView();

            // Add 50 Players button
            float bottomY = winY + winHeight - 42f;
            if (!ReplayDownloader.isFetchingEntries && GUI.Button(new Rect(winX + (winWidth - 160f) / 2f, bottomY, 160f, 30f), "Add 50 Players"))
            {
                int nextStart = ReplayDownloader.loadedEntries.Count + 1;
                ReplayDownloader.FetchLeaderboardRange(nextStart, nextStart + 49);
            }

            // Draw Tooltip in a fixed box TO THE RIGHT of the main Leaderboard Window
            if (shouldShowTooltip)
            {
                float tooltipWidth = 220f;
                float tooltipHeight = 110f;
                float tooltipX = winX + winWidth + 10f; // Fixed position 10px to the right of leaderboard window
                float tooltipY = winY + 30f;

                Rect tooltipBoxRect = new Rect(tooltipX, tooltipY, tooltipWidth, tooltipHeight);

                GUI.Box(tooltipBoxRect, "");
                GUIStyle tooltipTextStyle = new GUIStyle(GUI.skin.label)
                {
                    wordWrap = true,
                    fontSize = 11,
                    alignment = TextAnchor.MiddleCenter
                };
                tooltipTextStyle.normal.textColor = Color.yellow;

                GUI.Label(new Rect(tooltipBoxRect.x + 8f, tooltipBoxRect.y + 8f, tooltipBoxRect.width - 16f, tooltipBoxRect.height - 16f),
                    "Selecting more than 166 ghost cars exceeds Unity's audio engine channel limits. Engine sound effects may stutter, glitch, or turn completely silent.", tooltipTextStyle);
            }
        }

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