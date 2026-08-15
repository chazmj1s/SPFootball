using SaturdayPulse.Api.Models;

namespace SaturdayPulse.Api.Configuration
{
    public static class PowerRatingConfiguration
    {
        public static readonly List<TierAdjustmentRule> TierRules = new()
        {
            // --- CORE TIER 1 CONFERENCES (Open-ended) ---
            new TierAdjustmentRule { ConferenceAbbreviation = "SEC", IsTier1 = true },
            new TierAdjustmentRule { ConferenceAbbreviation = "Big Ten", IsTier1 = true },
            new TierAdjustmentRule { ConferenceAbbreviation = "Big 12", IsTier1 = true },
            new TierAdjustmentRule { ConferenceAbbreviation = "ACC", IsTier1 = true },

            // --- HISTORICAL TIER 1 CONFERENCES (Naturally Defunct) ---
            new TierAdjustmentRule { ConferenceAbbreviation = "SWC", IsTier1 = true },
            new TierAdjustmentRule { ConferenceAbbreviation = "Big 8", IsTier1 = true },
            new TierAdjustmentRule { ConferenceAbbreviation = "Big East", IsTier1 = true },

            // --- THE PACIFIC REALIGNMENT EXCEPTION (Closed Window) ---
            new TierAdjustmentRule { ConferenceAbbreviation = "Pac-12", StartYear = 1965, EndYear = 2023, IsTier1 = true },
            new TierAdjustmentRule { ConferenceAbbreviation = "Pac-10", StartYear = 1965, EndYear = 2023, IsTier1 = true },
            new TierAdjustmentRule { ConferenceAbbreviation = "Pac-8", StartYear = 1965, EndYear = 2023, IsTier1 = true },

            // --- MAJOR INDEPENDENTS (Open-ended vs Closed Windows) ---
            new TierAdjustmentRule { TeamId = 87, ConferenceAbbreviation = "IND", IsTier1 = true }, // Notre Dame (Permanent)
            new TierAdjustmentRule { TeamId = 213, ConferenceAbbreviation = "IND", StartYear = 1965, EndYear = 1992, IsTier1 = true }, // Penn State
            new TierAdjustmentRule { TeamId = 2390, ConferenceAbbreviation = "IND", StartYear = 1965, EndYear = 1990, IsTier1 = true }, // Miami
            new TierAdjustmentRule { TeamId = 52, ConferenceAbbreviation = "IND", StartYear = 1965, EndYear = 1991, IsTier1 = true },   // Florida State
            new TierAdjustmentRule { TeamId = 221, ConferenceAbbreviation = "IND", StartYear = 1965, EndYear = 1990, IsTier1 = true },  // Pittsburgh (Joined Big East in 1991)
            new TierAdjustmentRule { TeamId = 277, ConferenceAbbreviation = "IND", StartYear = 1965, EndYear = 1990, IsTier1 = true },  // West Virginia (Joined Big East in 1991)
            new TierAdjustmentRule { TeamId = 103, ConferenceAbbreviation = "IND", StartYear = 1965, EndYear = 1990, IsTier1 = true },  // Boston College (Joined Big East in 1991)
            new TierAdjustmentRule { TeamId = 259, ConferenceAbbreviation = "IND", StartYear = 1965, EndYear = 1990, IsTier1 = true },  // Virginia Tech (Joined Big East in 1991)
            new TierAdjustmentRule { TeamId = 164, ConferenceAbbreviation = "IND", StartYear = 1965, EndYear = 1990, IsTier1 = true },  // Rutgers (Joined Big East in 1991)
            new TierAdjustmentRule { TeamId = 218, ConferenceAbbreviation = "IND", StartYear = 1965, EndYear = 1990, IsTier1 = true },  // Temple (Joined Big East in 1991)
            new TierAdjustmentRule { TeamId = 2579, ConferenceAbbreviation = "IND", StartYear = 1971, EndYear = 1991, IsTier1 = true }, // South Carolina (Left ACC in '71, Joined SEC in '92)
            new TierAdjustmentRule { TeamId = 59, ConferenceAbbreviation = "IND", StartYear = 1965, EndYear = 1978, IsTier1 = true },   // Georgia Tech (Left SEC in '64, Joined ACC in '79)
            new TierAdjustmentRule { TeamId = 258, ConferenceAbbreviation = "IND", StartYear = 1965, EndYear = 1977, IsTier1 = true },  // Virginia (Joined ACC in 1978)
            new TierAdjustmentRule { TeamId = 349, ConferenceAbbreviation = "IND", StartYear = 1965, EndYear = 1997, IsTier1 = true },  // Army (Traditional Power Era)
            new TierAdjustmentRule { TeamId = 2426, ConferenceAbbreviation = "IND", StartYear = 1965, EndYear = 2014, IsTier1 = true }, // Navy (Traditional Power Era; Joined AAC in 2015)
            new TierAdjustmentRule { TeamId = 2005, ConferenceAbbreviation = "IND", StartYear = 1965, EndYear = 1979, IsTier1 = true }, // Air Force (Joined WAC in 1980)
            new TierAdjustmentRule { TeamId = 252, ConferenceAbbreviation = "IND", StartYear = 2011, EndYear = 2022, IsTier1 = true }   // BYU (Modern Era Power Independent; Joined Big 12 in 2023)
        };
    }

}
