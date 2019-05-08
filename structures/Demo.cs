﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Runtime.InteropServices;
using DemoCleaner2.DemoParser.parser;
using System.Text.RegularExpressions;
using System.Globalization;
using DemoCleaner2.DemoParser.huffman;

namespace DemoCleaner2
{
    public class Demo
    {
        public string mapName;
        public string modphysic;
        public string timeString;
        public TimeSpan time;
        public string playerName;
        public string country;
        public FileInfo file;
        public bool hasError;
        public DateTime? recordTime;

        string dfType;
        string physic;
        string modNum;

        string validity = "";
        public bool useValidation = true;

        public string errSymbol = "&_";

        public RawInfo rawInfo = null;

        public string demoNewName {
            get {
                if (hasError) {
                    return file.Name.StartsWith(errSymbol) ? file.Name : errSymbol + file.Name;
                }

                string demoname = "";
                string playerCountry = country.Length > 0 ? playerName + "." + country : playerName;

                if (time.TotalMilliseconds > 0) {
                    //если есть тайм, то пишем нормальный name для демки
                    demoname = string.Format("{0}[{1}]{2:D2}.{3:D2}.{4:D3}({5})",
                    mapName, modphysic, (int) time.TotalMinutes, time.Seconds, time.Milliseconds, playerCountry);
                } else {
                    //если нет тайма, то мучаемся с генерацией текста
                    string oldName = file.Name;
                    oldName = oldName.Substring(0, oldName.Length - file.Extension.Length); //убираем расширение
                    oldName = removeSubstr(oldName, mapName);                               //убираем имя карты
                    oldName = removeSubstr(oldName, playerName);                            //убираем имя игрока
                    oldName = removeSubstr(oldName, country);                               //убираем страну
                    oldName = removeSubstr(oldName, modphysic);                             //убираем мод с физикой 
                    oldName = removeSubstr(oldName, physic);                                //убираем физику
                    oldName = removeSubstr(oldName, validity);                              //убираем строки валидации
                    oldName = removeDouble(oldName);                                        //убираем двойные символы (кроме  скобочек)
                    oldName = oldName.Replace("[]", "").Replace("()", "");                  //убираем пустые скобки
                    oldName = Regex.Replace(oldName, "(^[^[a-zA-Z0-9\\(\\)\\]\\[]|[^[a-zA-Z0-9\\(\\)\\]\\[]$)", "");    //убираем хрень в начале и в конце
                    demoname = string.Format("{0}{1}[{2}]{3}({4})", errSymbol, mapName, modphysic, oldName, playerCountry);
                }

                if (useValidation && validity.Length > 0) {
                    demoname = demoname + "{" + validity + "}"; //добавляем инфу о валидации
                }

                return demoname + file.Extension;
            }
        }

        //убирание двойных небуквенных символов и замена на первый
        //например: test__abc-_.xy -> test_abc-xy
        string removeDouble(string input)
        {
            var dup = Regex.Match(input, "[^[a-zA-Z0-9\\(\\)\\]\\[]{2,}");
            if (dup.Success && dup.Groups.Count > 0) {
                var symbol = dup.Groups[0].Value[0];
                return removeDouble(input.Substring(0, dup.Groups[0].Index) + symbol + input.Substring(dup.Groups[0].Index + dup.Groups[0].Length));
            } else {
                return input;
            }
        }

        //убирание подстроки с граничащими символами например: test_abc.xy -> test.xy
        //берётся последний символ если их 2, и первый если только слева: test_abcxy -> test_xy
        string removeSubstr(string input, string include)
        {
            if (!input.Contains(include) || include.Length == 0) {
                return input;
            }
            var symbol = "";

            int cropstart = 0;
            int cropend = 0;
            int pos = input.IndexOf(include);
            if (pos > 0) {
                symbol = input[pos - 1] + "";
                cropstart = char.IsLetterOrDigit(input[pos - 1]) ? 0 : 1;
            }
            if (pos + include.Length + 1 < input.Length) {
                cropend = char.IsLetterOrDigit(input[pos + include.Length]) ? 0 : 1;
                symbol = input[pos + include.Length] + "";
            }
            if (symbol == ")" || symbol == "]" || symbol == "}") {
                symbol = "_";
            }
            return input.Substring(0, pos - cropstart) + symbol + input.Substring(pos + include.Length + cropend);
        }

        //Получаем из имени файла детали демки
        public static Demo GetDemoFromFile(FileInfo file)
        {
            Demo demo = new Demo();
            demo.file = file;
            demo.recordTime = demo.file.CreationTime;

            var sub = file.Name.Split("[]()".ToArray());
            if (sub.Length >= 4) {
                //Карта
                demo.mapName = sub[0];

                //Физика
                demo.modphysic = sub[1];
                if (demo.modphysic.Length < 3) {
                    demo.hasError = true;
                }

                //Время
                demo.timeString = sub[2];
                var times = demo.timeString.Split('-', '.');
                try {
                    demo.time = getTimeSpan(demo.timeString);
                } catch (Exception) {
                    demo.hasError = true;
                }

                //Имя + страна
                var name = sub[3].Split('.');
                if (name.Length > 1) {
                    demo.playerName = name[0];
                    demo.country = name[1];
                } else {
                    demo.playerName = sub[3];
                }
            } else {
                demo.hasError = true;
            }
            return demo;
        }

        //обработка группировки, если нажата галка с обработкой мдф как дф
        public static string mdfToDf(string mod, bool processIt)
        {
            if (processIt && mod[0] == 'm') {
                return mod.Substring(1);
            }
            return mod;
        }

        public static Demo GetDemoFromFileRaw(FileInfo file)
        {
            Q3HuffmanMapper.init();
            var raw = Q3DemoParser.getRawConfigStrings(file.FullName);
            return Demo.GetDemoFromRawInfo(raw);
        }

        //Получаем заполненную демку из детальной инфы выдернутой из демки
        public static Demo GetDemoFromRawInfo(RawInfo raw)
        {
            var file = new FileInfo(raw.demoPath);

            var frConfig = raw.getFriendlyInfo();

            Demo demo = new Demo();

            demo.rawInfo = raw;

            //файл
            demo.file = file;
            if (frConfig.Count == 0  || !frConfig.ContainsKey(RawInfo.keyClient)) {
                demo.hasError = true;
                return demo;
            }

            //Имя
            string dfName = null;
            string uName = null;

            if (frConfig.ContainsKey(RawInfo.keyPlayer)) {
                dfName = frConfig[RawInfo.keyPlayer]["dfn"];
                uName = frConfig[RawInfo.keyPlayer]["n"];

                if (dfName == null || dfName.Length == 0 || dfName == "UnnamedPlayer") {
                    var n = uName;
                    if (n != null) {
                        n = Regex.Replace(n, "(\\^[0-9])", "");
                        demo.playerName = n;
                    } else {
                        demo.playerName = dfName;
                    }
                } else {
                    demo.playerName = dfName;
                }
            }

            //оффлайн тайм
            if (raw.performedTimes.Count == 1) {
                demo.time = getTimeSpanForDemo(raw.performedTimes[0]);
                if (raw.dateStamps.Count > 0) {
                    demo.recordTime = getDateForDemo(raw.dateStamps[0]);
                }
            } else if (raw.performedTimes.Count > 1) {
                double maxMillis = double.MaxValue;
                for (int i = 0; i < raw.performedTimes.Count; i++) {
                    var time = getTimeSpanForDemo(raw.performedTimes[i]);
                    if (time.TotalMilliseconds < maxMillis) {
                        demo.time = time;
                        if (i < raw.dateStamps.Count) {
                            demo.recordTime = getDateForDemo(raw.dateStamps[i]);
                        }
                    }
                }
            }

            //онлайн тайм
            if (raw.onlineTimes.Count == 1) {
                demo.playerName = getOnlineName(raw.onlineTimes[0]);
                demo.time = getOnlineTimeSpan(raw.onlineTimes[0]);
            } else if (raw.onlineTimes.Count > 1) {
                double maxMillis = double.MaxValue;
                foreach (var timeString in raw.onlineTimes) {
                    var onlineName = getOnlineName(raw.onlineTimes[0]);
                    if (onlineName == dfName || onlineName == uName) {
                        var time = getOnlineTimeSpan(timeString);
                        if (time.TotalMilliseconds < maxMillis) {
                            demo.time = time;
                        }
                    }
                }
            }

            //Карта
            demo.mapName = frConfig[RawInfo.keyClient]["mapname"];

            //геймтайп
            int gType = int.Parse(frConfig[RawInfo.keyClient]["defrag_gametype"]);
            switch (gType) {
                case 1: demo.dfType = "df"; break;
                case 2: demo.dfType = "fs"; break;
                case 3: demo.dfType = "fc"; break;

                case 5: demo.dfType = "mdf"; break;
                case 6: demo.dfType = "mfs"; break;
                case 7: demo.dfType = "mfc"; break;
            }

            //промод
            var promode = frConfig[RawInfo.keyClient]["df_promode"];
            demo.physic = int.Parse(promode) == 1 ? "cpm" : "vq3";                  //vq3, cpm

            //мод для fastcaps и freestyle
            var gametype = int.Parse(frConfig[RawInfo.keyClient]["defrag_mode"]);   //>=0 = mode
            demo.modNum = (gType != 1 && gType != 5) ? string.Format(".{0}", gametype) : "";         //.0 - .7

            //комбинируем
            demo.modphysic = string.Format("{0}.{1}{2}", demo.dfType, demo.physic, demo.modNum);

            //если есть читы, то пишем в демку
            demo.validity = checkValidity(frConfig);

            demo.country = tryGetCountryFromFilename(demo.file);
            return demo;
        }


        //Пытаемся получить страну, и только её, из имени самой демки 
        static string tryGetCountryFromFilename(FileInfo file) {
            String name = file.Name;
            name = name.Substring(0, name.Length - file.Extension.Length);

            int c1 = 0;
            int c2 = 0;
            string country = "";

            while (c1 >= 0 && c2 >= 0) {
                c1 = name.LastIndexOf(")");
                c2 = name.LastIndexOf("(");

                if (c1 > c2) {
                    country = tryGetCountryFromBrackets(name.Substring(c2 + 1, c1 - c2 - 1));
                    if (country.Length > 0) {
                        return country;
                    }
                    name = name.Substring(0, c2);
                } else {
                    name = name.Substring(0, c1 + 1);
                }
            } 
            return country;
        }

        static string tryGetCountryFromBrackets(string partname)
        {
            var p = partname.Replace("(", "").Replace(")", "");
            int i = p.LastIndexOf('.');
            if (i > 0 && i +1 < p.Length) {
                return p.Substring(i+1, p.Length - i - 1);
            } else return "";
        }

        //получение времени из онлайн надписи
        static string getOnlineName(string demoTimeCmd)
        {
            //print \"Rom^7 reached the finish line in ^23:38:208^7\n\"
            demoTimeCmd = Regex.Replace(demoTimeCmd, "(\\^[0-9]|\\\"|\\n|\")", "");          //print Rom reached the finish line in 3:38:208
            string name = demoTimeCmd.Substring(6, demoTimeCmd.LastIndexOf(" reached") - 6); //Rom
            return name;
        }

        static TimeSpan getOnlineTimeSpan(string demoTimeCmd) {
            //print \"Rom^7 reached the finish line in ^23:38:208^7\n\"
            demoTimeCmd = Regex.Replace(demoTimeCmd, "(\\^[0-9]|\\\"|\\n|\")", "");          //print Rom reached the finish line in 3:38:208
            string demoTime = demoTimeCmd.Substring(demoTimeCmd.LastIndexOf("in") + 3);      //3:38:208
            return getTimeSpan(demoTime);
        }

        //получение времени из онффлайн надписи
        static TimeSpan getTimeSpanForDemo(string demoTimeCmd)
        {
            //print "Time performed by ^2uN-DeaD!Enter^7 : ^331:432^7 (v1.91.23 beta)\n"

            //TODO проверить, что будет если в df_name будет со спецсимволами, например двоеточие, вопрос, кавычки
            demoTimeCmd = Regex.Replace(demoTimeCmd, "(\\^[0-9]|\\\"|\\n|\")", "");     //print Time performed  by uN-DeaD!Enter : 31:432 (v1.91.23 beta)

            demoTimeCmd = demoTimeCmd.Substring(demoTimeCmd.IndexOf(':') + 2);      //31:432 (v1.91.23 beta)
            demoTimeCmd = demoTimeCmd.Substring(0, demoTimeCmd.IndexOf(' ')).Trim();    //31:432

            return getTimeSpan(demoTimeCmd);
        }

        //получение времени из строки
        static TimeSpan getTimeSpan(string timeString)
        {
            var times = timeString.Split('-', '.', ':').Reverse().ToList();
            //так как мы реверснули таймы, милисекунды будут в начале
            return TimeSpan.Zero
                .Add(TimeSpan.FromMilliseconds  (times.Count > 0 ? int.Parse(times[0]) : 0))
                .Add(TimeSpan.FromSeconds       (times.Count > 1 ? int.Parse(times[1]) : 0))
                .Add(TimeSpan.FromMinutes       (times.Count > 2 ? int.Parse(times[2]) : 0));
            //делаем через таймспаны чтобы если минут больше 60, они корректно добавлялись
        }

        //получение даты записи демки если она есть
        static DateTime getDateForDemo(string s)
        {
            //print "Date: 10-25-14 02:43\n"
            string dateString = s.Substring(13).Replace("\n", "").Replace("\"", "").Trim();

            return DateTime.ParseExact(dateString, "MM-dd-yy HH:mm", CultureInfo.InvariantCulture);
        }


        //проверка демки на валидность
        static string checkValidity(Dictionary<string, Dictionary<string, string>> frConfig) {
            if (!frConfig.ContainsKey(RawInfo.keyGame)) {
                return "";
            }

            var kGame = frConfig[RawInfo.keyGame];
            
            var gametype = int.Parse(frConfig[RawInfo.keyClient]["defrag_gametype"]);
            var online = gametype > 3;
            string res;

            var fs = (gametype == 2 || gametype == 6);
            if (!fs) {
                res = checkKey(kGame, "sv_cheats", 0);              if (res.Length > 0) return res;
                res = checkKey(kGame, "df_mp_interferenceoff", 3);  if (res.Length > 0) return res;
            }

            res = checkKey(kGame, "timescale", 1);                  if (res.Length > 0) return res;
            res = checkKey(kGame, "defrag_svfps", 125, "sv_fps");   if (res.Length > 0) return res;
            res = checkKey(kGame, "g_knockback", 1000);             if (res.Length > 0) return res;
            res = checkKey(kGame, "g_gravity", 800);                if (res.Length > 0) return res;
            res = checkKey(kGame, "g_speed", 320);                  if (res.Length > 0) return res;
            res = checkKey(kGame, "pmove_msec", 8);                 if (res.Length > 0) return res;

            res = checkKey(kGame, "g_synchronousclients", (online ? 0 : 1), "g_sync"); if (res.Length > 0) return res;
            res = checkKey(kGame, "pmove_fixed", (online ? 1 : 0)); if (res.Length > 0) return res;
            return "";
        }

        //проверка ключа на валидность
        static string checkKey(Dictionary<string, string> keysGame, string key, int val, string errorString = "") {
            if (keysGame.ContainsKey(key) && keysGame[key].Length > 0) {
                int value = -1;
                var success = int.TryParse(keysGame[key], out value);
                if (!success || value != val) {
                    return (errorString.Length > 0 ? errorString : key) + "=" + keysGame[key];
                }
            }
            return "";
        }
    }
}