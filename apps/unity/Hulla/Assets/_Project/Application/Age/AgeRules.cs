using System;

namespace Hulla.Application.Age
{
    /// <summary>
    /// The age question at first launch (GDD 24.6, 25.4, #87): a birth year, nothing more. A year gives
    /// two possible ages, so the rules use the youngest, as the server does (`services/age.ts`).
    /// </summary>
    public static class AgeRules
    {
        public const int MinimumAge = 13;
        public const int AdultAge = 18;
        public const int EarliestYear = 1900;

        public static int YoungestAge(int birthYear, DateTime utcNow) => utcNow.Year - birthYear - 1;

        public static bool Valid(int birthYear, DateTime utcNow) => birthYear >= EarliestYear && birthYear <= utcNow.Year;

        public static bool TooYoung(int birthYear, DateTime utcNow) => YoungestAge(birthYear, utcNow) < MinimumAge;

        /// <summary>No year yet counts as under 18.</summary>
        public static bool UnderEighteen(int? birthYear, DateTime utcNow) =>
            birthYear == null || YoungestAge(birthYear.Value, utcNow) < AdultAge;
    }
}
