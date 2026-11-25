# Unity Profiler CSV Exporter

## What this tool does

Unity Profiler can show many spikes, and each frame can contain a deep hierarchy of samples. Finding the expensive samples by opening Profiler foldouts frame by frame can take time.

This Editor tool finds the slowest frames in the Profiler capture and exports their Main Thread sample hierarchies to CSV. Open the CSV in Excel to inspect the frame details more clearly, or give the CSV file to an AI assistant such as GitHub Copilot to analyze potential performance bottlenecks.

## How to use

1. Capture the session in Unity Profiler. For device performance, select the connected Player rather than Editor Play Mode.
2. Select a frame you want included in the report.
3. Choose **Tools > Profiler > Export Profiler CSV**.
4. Open the generated CSV in Excel for a clearer view, or give the CSV file to an AI assistant for analysis.

The report includes the frame selected in the Profiler and the 10 slowest Main Thread frames found in the capture. These are included independently, so if the selected frame is also among the 10 slowest, its frame number appears twice.

Reports are saved to `Library/ProfilerReports` with increasing names such as `ProfilerReport_0001.csv`. Existing reports are kept.

When imported into a Git repository, the editor tool adds its `Assets/UnityProfilerCSVExporter` folder and folder metadata to that repository's local `.git/info/exclude`. The repository's shared `.gitignore` is not changed.

## CSV contents and limitations

Each reported frame contains its Main Thread sample hierarchy, with frame number, sample depth and name, total time, self time, GC allocation, and call count. The report is a CSV file, not an Excel workbook, and currently contains **Main Thread only**; it is not a full-session export of every frame or every Profiler thread.

The CSV shows samples recorded by the Profiler capture. The detail available for analysis depends on the capture settings; Deep Profile may expose more managed call detail, but can add substantial profiling overhead.
