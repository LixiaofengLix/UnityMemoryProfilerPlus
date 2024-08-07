using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Unity.MemoryProfiler.Editor.UI;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.MemoryProfiler.Editor
{
    static class MemoryProfilerPlus
    {
        public static Action<CachedSnapshot, UnityObjectsModel.ItemData, UnityObjectsModel> UnityObjectInfosDumper =
            UnityObjectsDumper.UnityObjectInfos;
        
        public static Action<CachedSnapshot, UnityObjectsModel> AllUnityObjectInfosDumper =
            UnityObjectsDumper.AllUnityObjectInfos;
    }
    
    static class UnityObjectsDumper
    {
        public static CachedSnapshot m_Snapshot;

        private static Dictionary<string, Action<string, List<TreeViewItemData<UnityObjectsModel.ItemData>>>> _dumperTable = new Dictionary<string, Action<string, List<TreeViewItemData<UnityObjectsModel.ItemData>>>>();
        private static Dictionary<string, string> _headerTable;

        private static Action<string, List<TreeViewItemData<UnityObjectsModel.ItemData>>> _commonDumper = CommonDumper;
        
        public static void Register(string type, Action<string, List<TreeViewItemData<UnityObjectsModel.ItemData>>> dumper)
        {
            if ("Common".Equals(type))
                _commonDumper = dumper;
            else
                _dumperTable.Add(type, dumper);
        }
        
        public static void UnityObjectInfos(CachedSnapshot snapshot, UnityObjectsModel.ItemData selectedItemData, UnityObjectsModel model)
        {
            m_Snapshot = snapshot;
            
            if (selectedItemData.ChildCount == 0)
                return;
            // 获取对象列表
            List<TreeViewItemData<UnityObjectsModel.ItemData>> selectedGroup = new List<TreeViewItemData<UnityObjectsModel.ItemData>>();
            foreach (var node in model.RootNodes)
            {
                if (node.data.Name == selectedItemData.Name)
                    selectedGroup = node.children.ToList();
            }
            
            // 选择文件保存路径
            var selectedName = selectedItemData.Name;
            var toFile = MemoryProfilerTool.SelectSaveFilePath(selectedName, selectedItemData.ChildCount);
            if (toFile == "") return;
            
            // dumper
            var dumper = GetDumper(selectedName);
            dumper(toFile, selectedGroup);
        }

        public static void AllUnityObjectInfos(CachedSnapshot snapshot, UnityObjectsModel model)
        {
            m_Snapshot = snapshot;
            
            var saveFolder = EditorUtility.SaveFolderPanel("Save", "", "");

            var summaryFile = $"{saveFolder}/_Summary.csv";
            var f = new StreamWriter(summaryFile);
            f.WriteLine("TypeName,Count,TotalSize(Byte),TotalSize,NativeSize,ManagedSize,GpuSize");
            
            foreach (var node in model.RootNodes)
            {
                var rootData = node.data;
                
                var now = new DateTimeOffset(DateTime.Now).ToUnixTimeSeconds();
                var toFile = $"{saveFolder}/{rootData.Name}_{now}_{rootData.ChildCount}.csv";
                
                var dumper = GetDumper(rootData.Name);
                dumper(toFile, node.children.ToList());
                
                f.WriteLine($"{rootData.Name}," +
                            $"{rootData.ChildCount}," +
                            $"{rootData.TotalSize.Committed}," +
                            $"{MemoryProfilerTool.FormatByteSize(rootData.TotalSize.Committed)}," +
                            $"{MemoryProfilerTool.FormatByteSize(rootData.NativeSize.Committed)}," +
                            $"{MemoryProfilerTool.FormatByteSize(rootData.ManagedSize.Committed)}," +
                            $"{MemoryProfilerTool.FormatByteSize(rootData.GpuSize.Committed)}");
            }
            f.Close();
        }
        
        static void CommonDumper(string toFile, List<TreeViewItemData<UnityObjectsModel.ItemData>> selectedGroup)
        {
            var f = new StreamWriter(toFile);
            f.WriteLine("InstanceID,Addr,Name,TotalSize(Byte),TotalSize,NativeSize,ManagedSize,GpuSize");
            
            var total = selectedGroup.Count;
            var cur = 0;
            foreach (var itemData in selectedGroup)
            {
                MemoryProfilerTool.ShowProgress($"Dump Unity Objects", (float)cur / total, total, ++cur);
                
                var od = ObjectData.FromNativeObjectIndex(m_Snapshot, itemData.id);
                
                var addr = $"0x{od.GetObjectPointer(m_Snapshot, false).ToString("X")}";
                var totalSize = itemData.data.TotalSize;
                var name = itemData.data.Name;

                f.WriteLine($"{od.GetInstanceID(m_Snapshot)}," +
                            $"{addr}," +
                            $"{name}," +
                            $"{totalSize.Committed}," +
                            $"{MemoryProfilerTool.FormatByteSize(totalSize.Committed)}," +
                            $"{MemoryProfilerTool.FormatByteSize(itemData.data.NativeSize.Committed)}," +
                            $"{MemoryProfilerTool.FormatByteSize(itemData.data.ManagedSize.Committed)}," +
                            $"{MemoryProfilerTool.FormatByteSize(itemData.data.GpuSize.Committed)}");
            }
            MemoryProfilerTool.ClearProgressBar();
            f.Close();
        }

        public static Action<string, List<TreeViewItemData<UnityObjectsModel.ItemData>>> GetDumper(string key)
        {
            return _dumperTable.GetValueOrDefault(key, _commonDumper);
        }
    }

    // TODO All Memory 
    /*static class AllTrackedMemoryDumper
    {
        public static CachedSnapshot m_Snapshot;
        
        // 找到所有指定类型的托管对象列表
        private static Dictionary<string, List<long>> m_TypeNameToManagedIndexMap;

        private static Dictionary<string, Action<string, List<long>>> _dumperTable = new Dictionary<string, Action<string, List<long>>>();
        
        public static void CacheTypeNameToManagedIndexMap(Dictionary<string, List<long>> typeNameToManagedIndexMap)
        {
            m_TypeNameToManagedIndexMap = typeNameToManagedIndexMap;
        }
        
        public static void Register(string type, Action<string, List<long>> dumper)
        {
            _dumperTable.Add(type, dumper);
        }
        
        public static void AllTrackedMemoryInfos(CachedSnapshot snapshot, AllTrackedMemoryModel.ItemData selectedItemData, AllTrackedMemoryModel model)
        {
            m_Snapshot = snapshot;
            if (!m_TypeNameToManagedIndexMap.TryGetValue(selectedItemData.Name, out var indexes) || indexes.Count <= 0)
                return;
            
            // 选择文件保存路径
            var toFile = MemoryProfilerTool.SelectSaveFilePath(selectedItemData.Name, indexes.Count);
            if (toFile == "") return;
            
            var dumper = GetDumper(selectedItemData.Name);
            dumper(toFile, indexes);
        }

        static void CommonDumper(string toFile, List<long> indexes)
        {
            var od = ObjectData.FromManagedObjectIndex(m_Snapshot, (int)indexes[0]);
            var selectedTypeName = od.GenerateTypeName(m_Snapshot);
            
            var f = new StreamWriter(toFile);
            f.WriteLine("Addr,Name,TotalSize(Byte),TotalSize(Format),OnlyOneReferencedBy,ReferencedBy(Class),ReferencedBy(Name),ReferencedBy(Addr)");
            
            var total = indexes.Count;
            var cur = 0;
            foreach (var index in indexes)
            {
                MemoryProfilerTool.ShowProgress($"Dump {selectedTypeName} Infos", (float)cur / total, total, ++cur);
                
                od = ObjectData.FromManagedObjectIndex(m_Snapshot, (int)index);
                var managedObjectInfo = od.GetManagedObject(m_Snapshot);

                var addr = $"0x{od.GetObjectPointer(m_Snapshot, false).ToString("X")}";
                var name = MemoryProfilerTool.GetDisplayName(m_Snapshot, od);
                var size = (ulong)managedObjectInfo.Size;
                
                var refByInfo = MemoryProfilerTool.GetReferencedByFirst(m_Snapshot, od, out ObjectData renBy);
                
                f.WriteLine($"{addr},{name},{size},{MemoryProfilerTool.FormatByteSize(size)},{refByInfo}");
            }
            MemoryProfilerTool.ClearProgressBar();
            f.Close();
        }

        static Action<string, List<long>> GetDumper(string key)
        {
            return _dumperTable.GetValueOrDefault(key, CommonDumper);
        }
    }*/

    static class MemoryProfilerTool
    {
     
        /// <summary>
        /// 格式化内存大小
        /// </summary>
        /// <param name="bytes"></param>
        /// <returns></returns>
        public static string FormatByteSize(ulong bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB", "TB" };
            int order = 0;
            double size = bytes;
            while (size >= 1024 && order < sizes.Length - 1)
            {
                order++;
                size = size / 1024;
            }

            return $"{size:0.#} {sizes[order]}";
        }
        
        /// <summary>
        /// 选择输出数据的保存路径
        /// </summary>
        /// <param name="typeName"></param>
        /// <param name="count"></param>
        /// <returns></returns>
        public static string SelectSaveFilePath(string typeName, int count)
        {
            var now = new DateTimeOffset(DateTime.Now).ToUnixTimeSeconds();
            var filename = $"{typeName}_{now}_{count}.csv";
            return EditorUtility.SaveFilePanel("Save", "", filename, "");
        }
        
        public static void ShowProgress(string msg, float val,int total,int cur)
        {
            EditorUtility.DisplayProgressBar(msg, $"Processing ({cur}/{total}), please wait...", val);
        }
        
        public static void ClearProgressBar()
        {
            EditorUtility.ClearProgressBar();
        }
    }

    class ReferenceSearcher
    {
        Dictionary<int, bool> _nativeVisited = new Dictionary<int, bool>();
        Dictionary<ulong, bool> _managedVisited = new Dictionary<ulong, bool>();

        public bool CheckReferencedBy(CachedSnapshot cachedSnapshot, ObjectData od, Func<ObjectData, bool> checker)
        {
            return CheckReferencedBy(cachedSnapshot, od, checker, 0);
        }
        
        private bool CheckReferencedBy(CachedSnapshot cachedSnapshot, ObjectData od, Func<ObjectData, bool> checker, int depth)
        {
            if (od.isManaged && _managedVisited.TryGetValue(od.hostManagedObjectPtr, out var value))
                return value;
            if (od.isNative && _nativeVisited.TryGetValue(od.nativeObjectIndex, out var value1))
                return value1;

            if (depth > 8)
                return false;
            
            var objectsConnectingTo = od.GetAllReferencingObjects(cachedSnapshot);
            if (objectsConnectingTo.Length == 0)
            {
                if (od.isManaged)
                    _managedVisited[od.hostManagedObjectPtr] = false;
                if (od.isNative)
                    _nativeVisited[od.nativeObjectIndex] = false;
                return false;
            }

            foreach (var connection in objectsConnectingTo)
            {
                if (connection.IsUnknownDataType() || connection.displayObject.dataType == ObjectDataType.Type) continue;

                if (checker(connection))
                {
                    if (od.isManaged)
                        _managedVisited[od.hostManagedObjectPtr] = true;
                    if (od.isNative)
                        _nativeVisited[od.nativeObjectIndex] = true;
                    return true;
                }

                if (CheckReferencedBy(cachedSnapshot, connection, checker, depth + 1))
                {
                    if (od.isManaged)
                        _managedVisited[od.hostManagedObjectPtr] = true;
                    if (od.isNative)
                        _nativeVisited[od.nativeObjectIndex] = true;
                    return true;
                }
            }
            
            if (od.isManaged)
                _managedVisited[od.hostManagedObjectPtr] = false;
            if (od.isNative)
                _nativeVisited[od.nativeObjectIndex] = false;
            return false;
        }
        
        public static void EnumerateReferencedBy(CachedSnapshot cachedSnapshot, ObjectData od, Func<ObjectData, bool> cb, int limit)
        {
            HashSet<ObjectData> visited = new HashSet<ObjectData>();
            Queue<ObjectData> queue = new Queue<ObjectData>();

            int count = 0;
            queue.Enqueue(od);
            while (queue.Count > 0 && count < limit)
            {
                var curObjectData = queue.Dequeue();
                if (!visited.Contains(curObjectData))
                    visited.Add(curObjectData);
                else
                    continue;

                var objectsConnectingTo = curObjectData.GetAllReferencingObjects(cachedSnapshot);
                if (objectsConnectingTo == null) continue;
                
                foreach (var connection in objectsConnectingTo)
                {
                    // we can skip something that is referencing a type as its just a static field holding a connection to the type
                    // might need to come back and reconsider this in the future
                    if (connection.IsUnknownDataType() || connection.displayObject.dataType == ObjectDataType.Type) continue;

                    if (cb(curObjectData))
                        return;
                    count++;
                    
                    queue.Enqueue(connection);
                }
            }
        }

    }
}
