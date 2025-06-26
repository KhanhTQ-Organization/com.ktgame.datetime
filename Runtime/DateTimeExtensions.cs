using System;

namespace com.ktgame.datetime
{
	public static class DateTimeExtensions
	{
		private static readonly DateTime UNIX_EPOCH = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

		public static DateTime UnixEpoch => UNIX_EPOCH;

		public static long ToUnixTime(this DateTime self)
		{
			return (long)self.Subtract(UNIX_EPOCH).TotalSeconds;
		}

		public static DateTime FromUnixTime(this long unixTime)
		{
			return UNIX_EPOCH.AddSeconds(unixTime);
		}

		public static long TotalDaysForTime(this DateTime self, DateTime time)
		{
			return (long)self.Subtract(time).TotalDays;
		}

		public static long TotalHoursForTime(this DateTime self, DateTime time)
		{
			return (long)self.Subtract(time).TotalHours;
		}

		public static long TotalMillisecondsForTime(this DateTime self, DateTime time)
		{
			return (long)self.Subtract(time).TotalMilliseconds;
		}

		public static long TotalMinutesForTime(this DateTime self, DateTime time)
		{
			return (long)self.Subtract(time).TotalMinutes;
		}

		public static long TotalSecondsForTime(this DateTime self, DateTime time)
		{
			return (long)self.Subtract(time).TotalSeconds;
		}

		public static string ToPattern(this DateTime self)
		{
			return self.ToString("yyyy/MM/dd HH:mm:ss");
		}

		public static string ToShortDatePattern(this DateTime self)
		{
			return self.ToString("yyyy/MM/dd");
		}

		public static string ToLongTimePattern(this DateTime self)
		{
			return self.ToString("HH:mm:ss");
		}

		public static string ToShortTimePattern(this DateTime self)
		{
			return self.ToString("HH:mm");
		}
	}
}
