using System;

namespace ZodiacSignGuardian.Core.Timing
{
    /// <summary>
    /// 시계 설정. reset_rules(daily·weekly 규칙과 시계 열)와 offline_reward(max_frame_sec)에서 온다.
    /// Core가 Data 형식을 View에 드러내지 않도록 값만 옮겨 담는다.
    /// </summary>
    public sealed class ClockConfig
    {
        /// <summary>표의 utc_offset_min (한국 540)</summary>
        public int UtcOffsetMin { get; }
        /// <summary>daily 규칙의 reset_hour (5)</summary>
        public int DailyResetHour { get; }
        /// <summary>weekly 규칙의 reset_hour (5)</summary>
        public int WeeklyResetHour { get; }
        /// <summary>weekly 규칙의 weekday. 월요일 = 0 … 일요일 = 6</summary>
        public int WeeklyWeekday { get; }
        /// <summary>sync_interval_min (5): Services가 다시 묻는 간격. Core는 값만 전한다.</summary>
        public int SyncIntervalMin { get; }
        /// <summary>anchor_valid_min (10): 동기화 누적 시간이 이보다 길면 재확인 필요</summary>
        public int AnchorValidMin { get; }
        /// <summary>max_rtt_sec (5): Services가 이보다 긴 왕복의 응답을 버린다. Core는 값만 전한다.</summary>
        public int MaxRttSec { get; }
        /// <summary>max_frame_sec (1): 한 프레임에서 전투에 인정하는 최대 시간</summary>
        public int MaxFrameSec { get; }

        public const long UsPerSec = 1_000_000L;
        public const long UsPerMin = 60L * UsPerSec;
        public const long UsPerHour = 60L * UsPerMin;
        public const long UsPerDay = 24L * UsPerHour;

        public ClockConfig(int utcOffsetMin, int dailyResetHour, int weeklyResetHour, int weeklyWeekday,
            int syncIntervalMin, int anchorValidMin, int maxRttSec, int maxFrameSec)
        {
            if (utcOffsetMin < -24 * 60 || utcOffsetMin > 24 * 60) throw new ArgumentOutOfRangeException(nameof(utcOffsetMin), utcOffsetMin, "UTC 차이는 ±1440분 안");
            if (dailyResetHour < 0 || dailyResetHour > 23) throw new ArgumentOutOfRangeException(nameof(dailyResetHour), dailyResetHour, "0~23");
            if (weeklyResetHour < 0 || weeklyResetHour > 23) throw new ArgumentOutOfRangeException(nameof(weeklyResetHour), weeklyResetHour, "0~23");
            if (weeklyWeekday < 0 || weeklyWeekday > 6) throw new ArgumentOutOfRangeException(nameof(weeklyWeekday), weeklyWeekday, "월 0 ~ 일 6");
            if (syncIntervalMin <= 0) throw new ArgumentOutOfRangeException(nameof(syncIntervalMin), syncIntervalMin, "양수");
            if (anchorValidMin <= 0) throw new ArgumentOutOfRangeException(nameof(anchorValidMin), anchorValidMin, "양수");
            if (anchorValidMin <= syncIntervalMin) throw new ArgumentOutOfRangeException(nameof(anchorValidMin), anchorValidMin, "sync_interval_min보다 커야 함 (검사 10과 같은 규칙)");
            if (maxRttSec <= 0) throw new ArgumentOutOfRangeException(nameof(maxRttSec), maxRttSec, "양수");
            if (maxFrameSec <= 0) throw new ArgumentOutOfRangeException(nameof(maxFrameSec), maxFrameSec, "양수");
            UtcOffsetMin = utcOffsetMin;
            DailyResetHour = dailyResetHour;
            WeeklyResetHour = weeklyResetHour;
            WeeklyWeekday = weeklyWeekday;
            SyncIntervalMin = syncIntervalMin;
            AnchorValidMin = anchorValidMin;
            MaxRttSec = maxRttSec;
            MaxFrameSec = maxFrameSec;
        }

        public long AnchorValidUs => AnchorValidMin * UsPerMin;
        public long MaxFrameUs => MaxFrameSec * UsPerSec;

        /// <summary>표의 weekday 글자(mon~sun)를 월 0 ~ 일 6으로.</summary>
        public static int WeekdayIndex(string weekday)
        {
            switch (weekday)
            {
                case "mon": return 0;
                case "tue": return 1;
                case "wed": return 2;
                case "thu": return 3;
                case "fri": return 4;
                case "sat": return 5;
                case "sun": return 6;
                default: throw new ArgumentException("요일은 mon~sun: " + weekday, nameof(weekday));
            }
        }
    }
}
