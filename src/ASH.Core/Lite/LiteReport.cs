using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using ASH.Core.Settings;

namespace ASH.Core.Lite;

/// <summary>lite\&lt;name&gt;_lite.report.tsv: before/after sizes, the level, what each step removed, the blobs, the density, the settings, the layer table, the origin shift and the units.</summary>
public static class LiteReport
{
    static readonly CultureInfo C = CultureInfo.InvariantCulture;
    static string Mb(long b) => (b / 1048576.0).ToString("0.00", C) + " MB";
    static string YesNo(bool b) => b ? "예" : "아니오";
    static string N(double v) => v.ToString("0.######", C);
    static void Cells(TextWriter w, params string[] cells) => w.WriteLine(string.Join("\t", cells.Select(c => (c ?? "").Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' '))));

    /// <summary>Users copy these lines into ash.settings.tsv: word for word.</summary>
    static void Origin(TextWriter w, LiteResult r)
    {
        void Row(params string[] cells) => Cells(w, cells);
        w.WriteLine();
        Row("[원점 이동]");
        Row("이동량 X", N(r.ShiftX));
        Row("이동량 Y", N(r.ShiftY));
        Row("설명", "경량 좌표 = 원본 좌표 - 이동량. 경량 도면의 범위: " + (r.Extents.IsEmpty ? "-" : $"({N(r.Extents.MinX)}, {N(r.Extents.MinY)}) ~ ({N(r.Extents.MaxX)}, {N(r.Extents.MaxY)})"));
        Row("ash.settings.tsv에 넣을 값", "경량 도면에 그린 경로를 원본 도면 좌표 기준 모델로 만들 때(원본에서 Origin 0이었다면):");
        Row("OriginX", N(-r.ShiftX));
        Row("OriginY", N(-r.ShiftY));
        Row("참고", "모델 좌표 = (도면 좌표 - Origin) x Scale. 원본에서 Origin (Ox, Oy)를 쓰던 프로젝트는 OriginX = Ox - 이동량 X, OriginY = Oy - 이동량 Y");
    }

    /// <summary>The report of a level write (LiteBuild.Write(w, level)). density = LiteBuild.Histogram(), elapsed = the whole command, read = what the reader saw (R22).</summary>
    public static void Write(TextWriter w, LiteBuild b, LiteResult r, LiteStat.Hist density, string sourcePath, long sourceBytes, string litePath, long liteBytes, DateTime now, TimeSpan elapsed, LiteReadLog? read = null)
    {
        void Row(params string[] cells) => Cells(w, cells);
        void Section(string title) { w.WriteLine(); Row("[" + title + "]"); }
        string Int(long v) => v.ToString(C);
        var src = b.Source;
        var t = b.Thresholds(r.Level);
        var run = b.Run(t);
        long estimate = b.Total(r.Level);
        var blobs = b.TopBlobs(t);

        Row("ASH 도면 경량화 보고서 (AGVLITE)");
        Row("항목", "값");
        Row("작성", now.ToString("yyyy-MM-dd HH:mm", C));
        Row("원본 도면", sourcePath);
        Row("원본 크기 (DWG)", sourceBytes > 0 ? $"{Mb(sourceBytes)} ({sourceBytes.ToString("N0", C)} bytes)" : "알 수 없음 (저장 안 된 도면)");
        Row("경량 DXF", litePath);
        Row("경량 크기 (DXF R12)", $"{Mb(liteBytes)} ({liteBytes.ToString("N0", C)} bytes)");
        Row("판정", liteBytes > LiteLevels.WarnBytes ? "10 MB 초과" : liteBytes > LiteLevels.TargetBytes ? "5~10 MB" : "목표 충족");
        Row("소요 시간", elapsed.TotalSeconds.ToString("0.0", C) + " s");
        Row("단위", Units(src.InsUnits));
        Row("쓰기 정밀도", $"소수 {LiteDxfWriter.Precision(src.InsUnits)}자리");
        Row("평면화", "모든 좌표의 Z = 0");

        Section("단계");
        Row("쓴 단계", Int(r.Level));
        Row("선택", b.Settings.Level != null ? "고정" : "자동");
        Row("목표", Mb(LiteLevels.TargetBytes));
        Row("예상 크기", Mb(estimate));
        if (run.Truncated > 0) Row("예산 초과", run.Truncated + "개 노드");
        if (density.Cut) Row("밀도 예산 초과", "예");
        if (liteBytes > LiteLevels.WarnBytes)
        {
            int i = 0;
            foreach (var x in blobs.Where(x => x.Instances > 0).Take(3)) Row("큰 덩어리 " + ++i, $"({N(x.X)}, {N(x.Y)}), 대각선 {N(x.DiagMm)} mm, 선 {x.Lines}개, 인스턴스 {x.Instances}개");
            i = 0;
            foreach (var (layer, bytes) in b.Biggest(t)) Row("큰 레이어 " + ++i, $"{layer}, {Mb(bytes)}");
        }
        Row("단계", "곡선 허용 오차 mm", "최소 크기 mm", "밀도 기준 mm/m²", "덩어리 최대 mm", "예상 크기");
        for (int i = 0; i <= LiteLevels.Max; i++)
        {
            var l = b.Thresholds(i);
            bool on = l.DenseMmPerM2 > 0;
            Row(Int(i), N(l.ToleranceMm), N(l.MinSizeMm), on ? N(l.DenseMmPerM2) : "끔", on ? N(l.MaxBlobMm) : "-", b.LevelSizes.TryGetValue(i, out var size) ? Mb(size) : "-");
        }

        Section("정리 단계");
        Row("단계", "제거 개수", "줄인 바이트");
        var s = run.Stats;
        foreach (var (name, step) in new[] { ("퇴화", s.Degenerate), ("중복", s.Duplicate), ("겹침", s.Overlap), ("작은 요소", s.Small), ("덩어리 외곽선", s.Blob) })
            Row(name, Int(step.Count), Int(step.Bytes));
        Row("인스턴스 사이 중복", Int(r.Deduped), Int(r.DedupedBytes));
        Row("덩어리", Int(s.Blobs));
        Row("덩어리 안 인스턴스", Int(s.DroppedInstances));
        Row("작은 인스턴스", Int(s.SmallInstances));
        Row("기준", "정의 단위, 인스턴스 사이 중복만 배치 단위");

        Section("덩어리 상위 30");
        Row("중심 X (원본)", "중심 Y (원본)", "대각선 mm", "선 밀도 mm/m²", "지운 선 수", "인스턴스 수");
        foreach (var x in blobs)
        {
            bool placed = x.Instances > 0;
            Row(placed ? N(x.X) : "-", placed ? N(x.Y) : "-", placed ? N(x.DiagMm) : "-", N(x.Density), Int(x.Lines), Int(x.Instances));
        }

        w.WriteLine();
        LiteStat.WriteHist(w, density);

        Section("설정");
        Row("단계 고정", b.Settings.Level is int fixedLevel ? Int(fixedLevel) : "자동");
        Row("제외 레이어", string.Join(", ", b.Settings.ExcludeLayers));
        Row("외곽선 끄기", YesNo(b.Settings.NoBlobs));

        Origin(w, r);

        if (read != null)
        {
            Section("원본 엔티티 (모델 공간 최상위)");
            Row("종류", "개수");
            foreach (var kv in read.Before) Row(kv.Key, Int(kv.Value));
            Row("합계", Int(read.Before.Values.Sum()));

            Section("제거한 항목 (정의 단위)");
            Row("종류", "개수");
            foreach (var kv in read.Removed) Row(kv.Key, Int(kv.Value));
            Row("3D 객체", Int(read.Solids.Sum()));
            Row("폭발 실패 (범위 상자로 대체)", Int(read.ExplodeFailed));
            Row("읽지 못한 엔티티", Int(read.Failures));
        }

        Section("경량 도면 엔티티");
        Row("종류", "개수");
        foreach (var kv in r.Written) Row(kv.Key, Int(kv.Value));
        Row("합계", Int(r.Entities));

        Section("레이어");
        Row("레이어", "제외", "엔티티", "예상 크기 (KB)");
        var tally = b.Estimate(t);
        foreach (var layer in src.Layers.Keys.Concat(tally.Keys).Distinct(StringComparer.Ordinal).OrderBy(l => l, StringComparer.Ordinal))
        {
            tally.TryGetValue(layer, out var v);
            Row(layer, YesNo(b.Excluded(layer)), Int(v.Count), (v.Bytes / 1024.0).ToString("0.0", C));
        }
    }

    /// <summary>INSUNITS as text plus the matching Scale. Geometry is never rescaled: ASH's Scale setting converts units.</summary>
    public static string Units(int insUnits)
    {
        string name = insUnits switch { 0 => "단위 없음(mm로 간주)", 1 => "inch", 2 => "feet", 4 => "mm", 5 => "cm", 6 => "m", 7 => "km", 10 => "yard", _ => "기타" };
        return $"INSUNITS {insUnits} = {name}. 좌표는 배율을 바꾸지 않고 그대로 씁니다. ash.settings.tsv의 Scale = {UnitScale.FromInsUnits(insUnits).ToString("R", C)} (도면 1단위의 m). R12 DXF에는 단위 값이 없으므로 경량 도면 폴더의 ash.settings.tsv Scale도 이 값인지 확인하세요";
    }
}
