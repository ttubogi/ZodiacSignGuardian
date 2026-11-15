using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;
using ZodiacSignGuardian.Data.Tables;

namespace ZodiacSignGuardian.Core.Tests.Data
{
    /// <summary>
    /// 테스트용 작은 표 묶음. 저장소의 Tables/csv와 따로 둔다 (임시 값이 바뀌어도 테스트가 흔들리지 않게).
    /// 모든 검사를 통과하고, v1 표(요일 던전·시간대 이벤트·주간 도전)도 채워 검사 5·6이 실제로 돈다.
    /// </summary>
    internal static class TestTables
    {
        internal static Dictionary<string, string> Good()
        {
            var stars = new StringBuilder("star_id,element,name_ko,build\n");
            foreach (var e in Schemas.Elements)
            {
                for (int i = 1; i <= 3; i++) stars.Append(e + "_" + i + "," + e + "," + e + " " + i + ",m1\n");
            }
            stars.Append("wood_4,wood,wood 4,v1\n");

            return new Dictionary<string, string>
            {
                ["reset_rules.csv"] =
                    "rule_id,period,reset_hour,weekday,cycle_days,utc_offset_min,sync_interval_min,anchor_valid_min,max_rtt_sec\n" +
                    "daily,daily,5,,,540,5,10,5\n" +
                    "weekly,weekly,5,mon,,540,5,10,5\n" +
                    "attendance_cycle,cycle,,,3,540,5,10,5\n",
                ["daily_missions.csv"] =
                    "mission_id,reset_rule,condition,target_count,reward_currency,reward_amount,build\n" +
                    "kill_monsters,daily,kill_monster,100,gold,20,m1\n" +
                    "dungeon_runs,daily,clear_boss,1,dungeon_key,1,v1\n",
                ["attendance.csv"] =
                    "day,reward_kind,reward_id,amount\n" +
                    "1,currency,gold,50\n" +
                    "2,currency,dust,1000\n" +
                    "3,star,wood_1,1\n",
                ["offline_reward.csv"] =
                    "max_minutes,min_minutes,ad_multiplier,max_frame_sec\n" +
                    "720,1,2,1\n",
                ["weekday_dungeons.csv"] =
                    "weekday,dungeon_id,reward_currency,free_entries\n" +
                    string.Concat(Schemas.Weekdays.Select(w => w + ",dungeon_" + w + ",dungeon_key,2\n")),
                ["timed_events.csv"] =
                    "event_id,weekdays,start_time,end_time,reward_currency\n" +
                    "lunch,mon|wed,12:00,13:00,gold\n" +
                    "after_lunch,mon,13:00,14:00,gold\n" +       // lunch 끝과 맞닿음 (겹침 아님)
                    "dawn,sat,00:00,05:00,gold\n" +              // 하루 경계에서 끝남
                    "day_long,sat,05:00,24:00,gold\n",           // 하루 경계에서 시작, 자정에 끝남
                ["weekly_challenge.csv"] =
                    "min_stage,reward_currency,reward_amount\n" +
                    "10,dungeon_key,3\n",
                ["currencies.csv"] =
                    "currency_id,name_ko,number_type,build\n" +
                    "gold,금화,integer,m1\n" +
                    "dust,\"별가루, 반짝\",game_number,m1\n" +   // 따옴표 안 쉼표
                    "dungeon_key,던전 열쇠,integer,v1\n",
                ["stars.csv"] = stars.ToString(),
                ["boards.csv"] =
                    "chapter,constellation_id,slot_a,slot_b,build\n" +
                    "1,cassiopeia,1,2,m1\n1,cassiopeia,2,3,m1\n1,cassiopeia,3,4,m1\n1,cassiopeia,4,5,m1\n" +
                    "2,cygnus,1,2,v1\n2,cygnus,2,3,v1\n2,cygnus,2,4,v1\n2,cygnus,2,5,v1\n",
                ["stats.csv"] =
                    "stat_id,name_ko,max_level,build\n" +
                    "attack,공격력,0,m1\n",
                ["ad_buffs.csv"] =
                    "buff_id,multiplier,duration_min,build\n" +
                    "hunt_double,2,30,m1\n",
            };
        }

        internal static List<TableIssue> Run(Dictionary<string, string> texts)
        {
            return TableBuilder.BuildFromTexts(texts).Issues;
        }

        /// <summary>표의 글 일부를 바꾼다. 바꿀 글이 없으면 테스트 실패 (변형이 조용히 무시되지 않게).</summary>
        internal static Dictionary<string, string> Replace(this Dictionary<string, string> texts, string table, string from, string to)
        {
            var file = table + ".csv";
            Assert.That(texts[file].Contains(from), Is.True, file + "에 '" + from.Replace("\n", "\\n") + "'가 없음 (변형 실패)");
            texts[file] = texts[file].Replace(from, to);
            return texts;
        }

        internal static Dictionary<string, string> AppendRow(this Dictionary<string, string> texts, string table, string row)
        {
            var file = table + ".csv";
            texts[file] = texts[file] + row + "\n";
            return texts;
        }

        internal static Dictionary<string, string> HeaderOnly(this Dictionary<string, string> texts, string table)
        {
            var file = table + ".csv";
            texts[file] = texts[file].Split('\n')[0] + "\n";
            return texts;
        }

        internal static string Describe(IEnumerable<TableIssue> issues)
        {
            var list = issues.ToList();
            return list.Count == 0 ? "(문제 없음)" : string.Join("\n", list.Select(i => i.ToString()));
        }

        /// <summary>그 종류의 문제가 하나 이상 있고, 다른 종류의 문제는 없다. (검사가 너무 넓거나 좁지 않은지)</summary>
        internal static void AssertOnly(List<TableIssue> issues, TableIssueKind kind)
        {
            Assert.That(issues.Count, Is.GreaterThan(0), "위반을 잡지 못함");
            Assert.That(issues.All(i => i.Kind == kind), Is.True, TableIssue.KindLabel(kind) + "만 나와야 함:\n" + Describe(issues));
        }

        internal static void AssertHas(List<TableIssue> issues, TableIssueKind kind)
        {
            Assert.That(issues.Any(i => i.Kind == kind), Is.True, TableIssue.KindLabel(kind) + "이 없음:\n" + Describe(issues));
        }

        internal static void AssertNone(List<TableIssue> issues)
        {
            Assert.That(issues.Count, Is.EqualTo(0), Describe(issues));
        }
    }

    /// <summary>저장소 루트 찾기. Unity에서는 현재 폴더가 프로젝트 루트, dotnet test에서는 출력 폴더에서 위로 올라간다.</summary>
    internal static class RepoFiles
    {
        internal static string Root
        {
            get
            {
                foreach (var start in new[] { TestContext.CurrentContext.TestDirectory, Directory.GetCurrentDirectory() })
                {
                    var dir = new DirectoryInfo(start);
                    while (dir != null)
                    {
                        if (File.Exists(Path.Combine(dir.FullName, "AGENTS.md")) && Directory.Exists(Path.Combine(dir.FullName, "Tables")))
                        {
                            return dir.FullName;
                        }
                        dir = dir.Parent;
                    }
                }
                throw new InvalidOperationException("저장소 루트(AGENTS.md + Tables)를 찾지 못함");
            }
        }

        internal static string CsvDir => Path.Combine(Root, "Tables", "csv");
        internal static string JsonDir => Path.Combine(Root, "Tables", "json");

        internal static Dictionary<string, byte[]> CsvBytes()
        {
            return Directory.GetFiles(CsvDir, "*.csv").ToDictionary(p => Path.GetFileName(p), File.ReadAllBytes);
        }

        internal static Dictionary<string, string> CsvTexts()
        {
            return Directory.GetFiles(CsvDir, "*.csv").ToDictionary(p => Path.GetFileName(p), p => File.ReadAllText(p, new UTF8Encoding(false)));
        }

        internal static Dictionary<string, string> JsonTexts()
        {
            return Directory.GetFiles(JsonDir, "*.json").ToDictionary(p => Path.GetFileName(p), p => File.ReadAllText(p, new UTF8Encoding(false)));
        }
    }
}
