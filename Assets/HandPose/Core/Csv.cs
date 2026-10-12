using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace HandPose
{
    public static class Csv
    {
        public static readonly CultureInfo Culture = CultureInfo.InvariantCulture;
        public static string Number(double v) => Numeric.Finite(v) ? v.ToString("R",Culture) : "";
        public static string Flag(bool v) => v ? "true" : "false";
        public static string Line(params string[] cells)
        {
            var s=new StringBuilder();
            for(int i=0;i<cells.Length;i++)
            {
                if(i>0) s.Append(',');
                string c=cells[i] ?? "";
                if(c.IndexOfAny(new[] {',','"','\r','\n'})>=0) s.Append('"').Append(c.Replace("\"","\"\"")).Append('"');
                else s.Append(c);
            }
            return s.ToString();
        }
        public static IEnumerable<string[]> Rows(TextReader reader)
        {
            var cells=new List<string>(); var cell=new StringBuilder(); bool quoted=false, closed=false, started=false;
            int n;
            while((n=reader.Read())!=-1)
            {
                char c=(char)n;
                if(!started && cells.Count==0 && cell.Length==0 && c=='\uFEFF') continue;
                started=true;
                if(quoted)
                {
                    if(c=='"')
                    {
                        if(reader.Peek()=='"') { reader.Read(); cell.Append('"'); }
                        else { quoted=false; closed=true; }
                    }
                    else cell.Append(c);
                    continue;
                }
                if(c==',' || c=='\r' || c=='\n')
                {
                    cells.Add(cell.ToString()); cell.Clear(); closed=false;
                    if(c!=',')
                    {
                        if(c=='\r' && reader.Peek()=='\n') reader.Read();
                        yield return cells.ToArray(); cells.Clear(); started=false;
                    }
                }
                else if(c=='"' && cell.Length==0 && !closed) quoted=true;
                else if(closed || c=='"') throw new FormatException("Malformed CSV quoting.");
                else cell.Append(c);
            }
            if(quoted) throw new FormatException("Unterminated CSV field.");
            if(started || cells.Count>0 || cell.Length>0) { cells.Add(cell.ToString()); yield return cells.ToArray(); }
        }
    }

    public sealed class CsvTable : IDisposable
    {
        readonly StreamReader reader;
        readonly IEnumerator<string[]> rows;
        readonly Dictionary<string,int> columns=new Dictionary<string,int>();
        string[] current;
        public CsvTable(string path)
        {
            reader=new StreamReader(path,Encoding.UTF8); rows=Csv.Rows(reader).GetEnumerator();
            if(!rows.MoveNext()) { Dispose(); throw new FormatException("CSV header missing: "+path); }
            for(int i=0;i<rows.Current.Length;i++) columns.Add(rows.Current[i],i);
        }
        public bool Next()
        {
            if(!rows.MoveNext()) return false;
            current=rows.Current;
            if(current.Length!=columns.Count) throw new FormatException("CSV row/column count mismatch.");
            return true;
        }
        public string Get(string name) => columns.TryGetValue(name,out int i) ? current[i] : throw new FormatException("Missing CSV column: "+name);
        public double Double(string name)
        {
            double v=double.Parse(Get(name),Csv.Culture);
            if(!Numeric.Finite(v)) throw new FormatException("Non-finite CSV value: "+name);
            return v;
        }
        public bool Bool(string name) => bool.Parse(Get(name));
        public void Dispose() { rows?.Dispose(); reader?.Dispose(); }
    }
}
