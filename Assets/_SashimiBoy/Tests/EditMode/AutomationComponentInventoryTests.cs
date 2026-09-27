#if UNITY_EDITOR
using System;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

public sealed class AutomationComponentInventoryTests
{
    private GameObject root;

    [TearDown]
    public void Cleanup()
    {
        if (root != null) UnityEngine.Object.DestroyImmediate(root);
    }

    private object Inspect()
    {
        var type = AppDomain.CurrentDomain.GetAssemblies()
            .Select(assembly => assembly.GetType("SashimiBoy.EditorTools.AutomationComponentInventory"))
            .FirstOrDefault(candidate => candidate != null);
        Assert.That(type, Is.Not.Null, "Host inventory Editor helper must be compiled.");
        return type.GetMethod("Inspect").Invoke(null, new object[] { "Assets/Fixture.prefab", "Prefab", new[] { root } });
    }

    private static int Count(object report, string field) => (int)report.GetType().GetField(field).GetValue(report);

    private static void AddEventSystem(GameObject target)
    {
        var type = AppDomain.CurrentDomain.GetAssemblies()
            .Select(assembly => assembly.GetType("UnityEngine.EventSystems.EventSystem"))
            .First(candidate => candidate != null);
        target.AddComponent(type);
    }

    [Test]
    public void InventoryDetectsTwoActiveListenersAndEventSystems()
    {
        root = new GameObject("Fixture");
        for (int i = 0; i < 2; i++)
        {
            var child = new GameObject("Active " + i);
            child.transform.SetParent(root.transform);
            child.AddComponent<AudioListener>();
            AddEventSystem(child);
        }
        var report = Inspect();
        Assert.That(Count(report, "activeAudioListeners"), Is.EqualTo(2));
        Assert.That(Count(report, "activeEventSystems"), Is.EqualTo(2));
    }

    [Test]
    public void InventoryExcludesDisabledComponentsAndInactiveAncestors()
    {
        root = new GameObject("Fixture");
        root.AddComponent<AudioListener>().enabled = false;
        var child = new GameObject("Inactive branch");
        child.transform.SetParent(root.transform);
        child.AddComponent<AudioListener>();
        AddEventSystem(child);
        child.SetActive(false);
        var report = Inspect();
        Assert.That(Count(report, "activeAudioListeners"), Is.Zero);
        Assert.That(Count(report, "activeEventSystems"), Is.Zero);
    }
}
#endif
