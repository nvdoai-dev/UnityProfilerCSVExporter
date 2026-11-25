using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;

public static class ProfilerFrameReport
{
    private const int SlowFramesToExport = 10;
    private const string ReportFilePrefix = "ProfilerReport_";

    [MenuItem("Tools/Profiler/Export Profiler CSV")]
    private static void ExportSlowFramesReport()
    {
        try
        {
            var reportPath = ExportSlowFramesReportInternal();
            EditorUtility.DisplayDialog(
                "Profiler Report Exported",
                "Report saved to:\n" + reportPath,
                "OK");
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            EditorUtility.DisplayDialog(
                "Profiler Report Export Failed",
                exception.GetBaseException().Message,
                "OK");
        }
    }

    private static string ExportSlowFramesReportInternal()
    {
        var profilerDriver = FindType(
            "UnityEditorInternal.ProfilerDriver",
            "UnityEditor.Profiling.ProfilerDriver");
        var profilerWindow = FindType("UnityEditor.ProfilerWindow");
        var hierarchyType = FindType("UnityEditor.Profiling.HierarchyFrameDataView");
        var firstFrame = GetStaticInt(profilerDriver, "firstFrameIndex");
        var lastFrame = GetStaticInt(profilerDriver, "lastFrameIndex");
        var selectedFrame = GetSelectedFrameIndex(profilerWindow);
        if (firstFrame < 0 || lastFrame < firstFrame)
        {
            throw new InvalidOperationException(
                "Profiler has no captured frames. Start recording and capture at least one frame, " +
                "then export again. You can stop recording before exporting.");
        }
        if (selectedFrame < firstFrame || selectedFrame > lastFrame)
            throw new InvalidOperationException(
                $"Selected Profiler frame {selectedFrame} is outside the captured range " +
                $"{firstFrame}-{lastFrame}.");

        var getView = profilerDriver
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .FirstOrDefault(method =>
                method.Name == "GetHierarchyFrameDataView" &&
                method.GetParameters().Length == 5);
        if (getView == null)
            throw new MissingMethodException(profilerDriver.FullName, "GetHierarchyFrameDataView");

        var viewModes = hierarchyType.GetNestedType("ViewModes", BindingFlags.Public);
        if (viewModes == null)
            throw new MissingMemberException(hierarchyType.FullName, "ViewModes");

        var defaultMode = Enum.Parse(viewModes, "Default");
        var totalTimeColumn = GetStaticInt(hierarchyType, "columnTotalTime");
        var selfTimeColumn = GetStaticInt(hierarchyType, "columnSelfTime");
        var gcColumn = GetStaticInt(hierarchyType, "columnGcMemory");
        var callsColumn = GetStaticInt(hierarchyType, "columnCalls");
        var startFrame = firstFrame;
        var frames = new List<FrameSummary>();

        for (var frame = startFrame; frame <= lastFrame; frame++)
        {
            using (var view = CreateView(getView, frame, defaultMode, totalTimeColumn))
            {
                var rootId = InvokeInt(view, "GetRootItemID");
                var frameMs = GetColumn(view, rootId, totalTimeColumn);
                frames.Add(new FrameSummary(frame, frameMs));
            }
        }

        if (!frames.Any(frame => frame.Index == selectedFrame))
        {
            using (var view = CreateView(getView, (int)selectedFrame, defaultMode, totalTimeColumn))
            {
                var rootId = InvokeInt(view, "GetRootItemID");
                frames.Add(new FrameSummary(
                    (int)selectedFrame,
                    GetColumn(view, rootId, totalTimeColumn)));
            }
        }

        var selectedFrames = frames
            .OrderByDescending(frame => frame.TotalMs)
            .Take(SlowFramesToExport)
            .Concat(frames.Where(frame => frame.Index == selectedFrame))
            .OrderBy(frame => frame.Index)
            .ToList();

        var report = new StringBuilder();
        report.AppendLine("Unity Profiler slow-frame report");
        report.AppendLine("Selected Profiler frame number: " + (selectedFrame + 1));
        report.AppendLine(string.Format(
            CultureInfo.InvariantCulture,
            "Captured frame range: {0}-{1}; scanned: {2}",
            startFrame + 1,
            lastFrame + 1,
            frames.Count));
        report.AppendLine("Thread: Main Thread (thread index 0)");
        report.AppendLine();
        report.AppendLine(
            "Frame,MainThreadTotalMs,Depth,Sample,TotalMs,SelfMs,GCAllocKB,Calls");

        foreach (var frame in selectedFrames)
        {
            report.AppendLine(string.Format(
                CultureInfo.InvariantCulture,
                "{0},{1:F3},0,\"FRAME\",{1:F3},,,",
                frame.Index + 1,
                frame.TotalMs));

            using (var view = CreateView(getView, frame.Index, defaultMode, totalTimeColumn))
            {
                AppendSamples(
                    report,
                    view,
                    frame,
                    selfTimeColumn,
                    gcColumn,
                    callsColumn);
            }
        }

        var outputDirectory = Path.Combine(Directory.GetCurrentDirectory(), "Library", "ProfilerReports");
        Directory.CreateDirectory(outputDirectory);
        var sequence = GetNextExportSequence(outputDirectory);
        var reportFileName = string.Format(
            CultureInfo.InvariantCulture,
            "{0}{1:D4}.csv",
            ReportFilePrefix,
            sequence);
        var reportPath = Path.Combine(outputDirectory, reportFileName);
        File.WriteAllText(reportPath, report.ToString(), new UTF8Encoding(false));
        Debug.Log("Profiler report exported: " + reportPath);
        return reportPath;
    }

    private static int GetNextExportSequence(string outputDirectory)
    {
        var highestSequence = 0;
        var prefixLength = ReportFilePrefix.Length;

        foreach (var path in Directory.GetFiles(outputDirectory, ReportFilePrefix + "*.csv"))
        {
            var fileName = Path.GetFileNameWithoutExtension(path);
            if (fileName.Length <= prefixLength ||
                !int.TryParse(
                    fileName.Substring(prefixLength),
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out var sequence))
            {
                continue;
            }

            highestSequence = Math.Max(highestSequence, sequence);
        }

        return highestSequence + 1;
    }

    private static void AppendSamples(
        StringBuilder report,
        object view,
        FrameSummary frame,
        int selfTimeColumn,
        int gcColumn,
        int callsColumn)
    {
        var viewType = view.GetType();
        var childrenMethod = viewType.GetMethods()
            .FirstOrDefault(method =>
                method.Name == "GetItemChildren" &&
                method.GetParameters().Length == 2 &&
                method.GetParameters()[0].ParameterType == typeof(int));
        var nameMethod = viewType.GetMethod("GetItemName", new[] { typeof(int) });
        if (childrenMethod == null || nameMethod == null)
            throw new MissingMethodException(viewType.FullName, "GetItemChildren/GetItemName");

        var childIds = new List<int>();
        var stack = new Stack<SampleNode>();
        stack.Push(new SampleNode(InvokeInt(view, "GetRootItemID"), -1));

        while (stack.Count > 0)
        {
            var node = stack.Pop();
            var name = Convert.ToString(
                nameMethod.Invoke(view, new object[] { node.Id }),
                CultureInfo.InvariantCulture) ?? string.Empty;
            report.AppendLine(string.Format(
                CultureInfo.InvariantCulture,
                "{0},{1:F3},{2},\"{3}\",{4:F3},{5:F3},{6:F2},{7:F0}",
                frame.Index + 1,
                frame.TotalMs,
                node.Depth,
                EscapeCsv(name),
                GetColumn(view, node.Id, GetStaticInt(viewType, "columnTotalTime")),
                GetColumn(view, node.Id, selfTimeColumn),
                GetColumn(view, node.Id, gcColumn) / 1024f,
                GetColumn(view, node.Id, callsColumn)));

            childIds.Clear();
            childrenMethod.Invoke(view, new object[] { node.Id, childIds });
            for (var i = childIds.Count - 1; i >= 0; i--)
                stack.Push(new SampleNode(childIds[i], node.Depth + 1));
        }
    }

    private static IDisposable CreateView(
        MethodInfo getView,
        int frame,
        object defaultMode,
        int sortColumn)
    {
        return (IDisposable)getView.Invoke(
            null,
            new[] { (object)frame, 0, defaultMode, sortColumn, false });
    }

    private static float GetColumn(object view, int itemId, int column)
    {
        var method = view.GetType().GetMethod(
            "GetItemColumnDataAsFloat",
            new[] { typeof(int), typeof(int) });
        if (method == null)
            throw new MissingMethodException(view.GetType().FullName, "GetItemColumnDataAsFloat");
        return Convert.ToSingle(
            method.Invoke(view, new object[] { itemId, column }),
            CultureInfo.InvariantCulture);
    }

    private static int InvokeInt(object target, string methodName)
    {
        var method = target.GetType().GetMethod(methodName, Type.EmptyTypes);
        if (method == null)
            throw new MissingMethodException(target.GetType().FullName, methodName);
        return Convert.ToInt32(method.Invoke(target, null), CultureInfo.InvariantCulture);
    }

    private static int GetStaticInt(Type type, string memberName)
    {
        const BindingFlags flags = BindingFlags.Public | BindingFlags.Static;
        var field = type.GetField(memberName, flags);
        if (field != null)
            return Convert.ToInt32(field.GetValue(null), CultureInfo.InvariantCulture);

        var property = type.GetProperty(memberName, flags);
        if (property != null)
            return Convert.ToInt32(property.GetValue(null, null), CultureInfo.InvariantCulture);

        throw new MissingMemberException(type.FullName, memberName);
    }

    private static long GetSelectedFrameIndex(Type profilerWindowType)
    {
        var window = Resources.FindObjectsOfTypeAll(profilerWindowType)
            .OfType<EditorWindow>()
            .OrderByDescending(candidate => candidate == EditorWindow.focusedWindow)
            .FirstOrDefault();
        if (window == null)
        {
            throw new InvalidOperationException(
                "The Unity Profiler window is not open. Open Window > Analysis > Profiler, " +
                "capture at least one frame, then export again.");
        }

        var controllerInterface = profilerWindowType.GetInterfaces()
            .FirstOrDefault(type => type.Name == "IProfilerWindowController");
        var selectedFrame = controllerInterface?.GetProperty(
            "selectedFrameIndex",
            BindingFlags.Public | BindingFlags.Instance);
        if (selectedFrame == null)
            throw new MissingMemberException(
                profilerWindowType.FullName,
                "IProfilerWindowController.selectedFrameIndex");

        return Convert.ToInt64(
            selectedFrame.GetValue(window, null),
            CultureInfo.InvariantCulture);
    }

    private static Type FindType(params string[] fullNames)
    {
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            foreach (var fullName in fullNames)
            {
                var type = assembly.GetType(fullName);
                if (type != null)
                    return type;
            }
        }

        throw new TypeLoadException(
            "Unity Profiler API type was not found: " + string.Join(" or ", fullNames));
    }

    private static string EscapeCsv(string value)
    {
        return value.Replace("\"", "\"\"");
    }

    private sealed class FrameSummary
    {
        public readonly int Index;
        public readonly float TotalMs;

        public FrameSummary(int index, float totalMs)
        {
            Index = index;
            TotalMs = totalMs;
        }
    }

    private struct SampleNode
    {
        public readonly int Id;
        public readonly int Depth;

        public SampleNode(int id, int depth)
        {
            Id = id;
            Depth = depth;
        }
    }
}
