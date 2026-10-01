using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace WitchRusPatcher
{
    public static class CsvHelper
    {
        public static (List<string> Header, List<List<string>> Rows) ParseCsv(string text)
        {
            if (string.IsNullOrEmpty(text))
                return (new List<string>(), new List<List<string>>());

            text = text.TrimStart('\uFEFF');

            var allRows = new List<List<string>>();
            var currentRow = new List<string>();
            var currentField = new StringBuilder();
            bool inQuotes = false;

            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];

                if (inQuotes)
                {
                    if (c == '"')
                    {
                        if (i + 1 < text.Length && text[i + 1] == '"')
                        {
                            currentField.Append('"');
                            i++; // skip next quote
                        }
                        else
                        {
                            inQuotes = false;
                        }
                    }
                    else
                    {
                        currentField.Append(c);
                    }
                }
                else
                {
                    if (c == '"')
                    {
                        inQuotes = true;
                    }
                    else if (c == ',')
                    {
                        currentRow.Add(currentField.ToString());
                        currentField.Clear();
                    }
                    else if (c == '\r')
                    {
                        if (i + 1 < text.Length && text[i + 1] == '\n')
                            i++;

                        currentRow.Add(currentField.ToString());
                        currentField.Clear();
                        allRows.Add(currentRow);
                        currentRow = new List<string>();
                    }
                    else if (c == '\n')
                    {
                        currentRow.Add(currentField.ToString());
                        currentField.Clear();
                        allRows.Add(currentRow);
                        currentRow = new List<string>();
                    }
                    else
                    {
                        currentField.Append(c);
                    }
                }
            }

            if (currentField.Length > 0 || currentRow.Count > 0)
            {
                currentRow.Add(currentField.ToString());
                allRows.Add(currentRow);
            }

            if (allRows.Count == 0)
                return (new List<string>(), new List<List<string>>());

            var header = allRows[0];
            var rows = allRows.GetRange(1, allRows.Count - 1);
            return (header, rows);
        }

        public static string DumpCsv(List<string> header, List<List<string>> rows, bool crlf)
        {
            var sb = new StringBuilder();
            sb.Append('\uFEFF'); // BOM

            string lineTerminator = crlf ? "\r\n" : "\n";

            AppendCsvRow(sb, header);
            sb.Append(lineTerminator);

            foreach (var row in rows)
            {
                AppendCsvRow(sb, row);
                sb.Append(lineTerminator);
            }

            return sb.ToString();
        }

        private static void AppendCsvRow(StringBuilder sb, List<string> fields)
        {
            for (int i = 0; i < fields.Count; i++)
            {
                if (i > 0) sb.Append(',');
                string field = fields[i] ?? "";
                bool needQuotes = field.Contains(',') || field.Contains('"') || field.Contains('\n') || field.Contains('\r');
                if (needQuotes)
                {
                    sb.Append('"');
                    sb.Append(field.Replace("\"", "\"\""));
                    sb.Append('"');
                }
                else
                {
                    sb.Append(field);
                }
            }
        }
    }
}
