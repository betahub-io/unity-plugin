using UnityEngine;
using UnityEngine.Profiling;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System;
using System.Diagnostics;

namespace BetaHub
{
    public class PerformanceSampler : MonoBehaviour
    {
        [Header("Sampling Configuration")]
        public float sampleFrequency = 1.0f; // in seconds
        public float sampleDuration = 60.0f; // in seconds

        private float sampleTimer;
        private float durationTimer;
        private bool isSampling;
        private List<PerformanceSample> samples;
        private Process currentProcess;

        [Serializable]
        public class PerformanceSample
        {
            public float timestamp;
            public long totalAllocatedMemory;
            public long totalReservedMemory;
            public long totalUnusedReservedMemory;
            public long monoHeapSize;
            public long monoUsedSize;
            public long allocatedMemoryForGraphicsDriver;
            public float frameTime;
            public float cpuUsage;
        }

        void Start()
        {
            samples = new List<PerformanceSample>();
            currentProcess = Process.GetCurrentProcess();
            // StartSampling();
        }

        void Update()
        {
            if (isSampling)
            {
                sampleTimer += Time.unscaledDeltaTime;
                durationTimer += Time.unscaledDeltaTime;

                if (sampleTimer >= sampleFrequency)
                {
                    sampleTimer = 0;
                    CollectSample();
                }

                if (durationTimer >= sampleDuration)
                {
                    isSampling = false;
                    UnityEngine.Debug.Log("Sampling complete.");
                }
            }
        }

        void StartSampling()
        {
            sampleTimer = 0;
            durationTimer = 0;
            isSampling = true;
            samples.Clear();
            UnityEngine.Debug.Log("Sampling started.");
        }

        void CollectSample()
        {
            PerformanceSample sample = new PerformanceSample
            {
                timestamp = Time.unscaledTime,
                totalAllocatedMemory = Profiler.GetTotalAllocatedMemoryLong(),
                totalReservedMemory = Profiler.GetTotalReservedMemoryLong(),
                totalUnusedReservedMemory = Profiler.GetTotalUnusedReservedMemoryLong(),
                monoHeapSize = Profiler.GetMonoHeapSizeLong(),
                monoUsedSize = Profiler.GetMonoUsedSizeLong(),
                allocatedMemoryForGraphicsDriver = Profiler.GetAllocatedMemoryForGraphicsDriver(),
                frameTime = Time.unscaledDeltaTime,
                cpuUsage = GetCPUUsage()
            };
            samples.Add(sample);
        }

        float GetCPUUsage()
        {
            currentProcess.Refresh();
            return (float)(currentProcess.TotalProcessorTime.TotalMilliseconds / (Environment.ProcessorCount * sampleFrequency * 1000));
        }

        public void SaveSamplesToFile(string filePath)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("Timestamp, TotalAllocatedMemory, TotalReservedMemory, TotalUnusedReservedMemory, MonoHeapSize, MonoUsedSize, AllocatedMemoryForGraphicsDriver, FrameTime, CPUUsage");

            foreach (var sample in samples)
            {
                sb.AppendLine($"{sample.timestamp}, {sample.totalAllocatedMemory}, {sample.totalReservedMemory}, {sample.totalUnusedReservedMemory}, {sample.monoHeapSize}, {sample.monoUsedSize}, {sample.allocatedMemoryForGraphicsDriver}, {sample.frameTime}, {sample.cpuUsage}");
            }

            File.WriteAllText(filePath, sb.ToString());
            UnityEngine.Debug.Log("Samples saved to file: " + filePath);
        }

        public string GetSamplesAsJson()
        {
            return JsonUtility.ToJson(new { samples = samples }, true);
        }
    }
}