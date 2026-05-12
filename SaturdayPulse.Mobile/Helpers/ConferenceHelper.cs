namespace SaturdayPulse.Helpers
{
    /// <summary>
    /// Single source of truth for the ordered conference filter list used across all tabs.
    /// P4 conferences come first, then G5, then Independent, matching the backend tier logic.
    /// </summary>
    public static class ConferenceHelper
    {
        // Display name → ConferenceAbbr value used in the API/DB
        // These must match the ConferenceAbbr values returned by the /teams endpoint.
        public static readonly IReadOnlyList<(string Display, string Abbr)> OrderedConferences =
        [
            // P4
            ("ACC",          "ACC"),
            ("Big 12",       "Big 12"),
            ("Big Ten",      "Big Ten"),
            ("SEC",          "SEC"),
            // G5
            ("AAC",          "AAC"),
            ("Conf USA",     "C-USA"),
            ("MAC",          "MAC"),
            ("Mtn West",     "Mountain West"),
            ("Sun Belt",     "Sun Belt"),
            // Independent / Other
            ("Independent",  "Independent"),
        ];

        /// <summary>Flat display-name list prefixed with "All", for Pickers.</summary>
        public static List<string> FilterDisplayList(bool includeTierShortcuts = false)
        {
            var list = new List<string> { "All" };
            if (includeTierShortcuts)
            {
                list.Add("P4");
                list.Add("G5");
                list.Add("── Conf ──");
            }
            list.AddRange(OrderedConferences.Select(c => c.Display));
            return list;
        }

        /// <summary>Maps a display name back to its ConferenceAbbr (or returns the input unchanged).</summary>
        public static string DisplayToAbbr(string display)
        {
            var match = OrderedConferences.FirstOrDefault(c => c.Display == display);
            return match.Abbr ?? display;
        }

        /// <summary>Maps a ConferenceAbbr to its display name (or returns the input unchanged).</summary>
        public static string AbbrToDisplay(string abbr)
        {
            var match = OrderedConferences.FirstOrDefault(c => c.Abbr == abbr);
            return match.Display ?? abbr;
        }
    }
}
