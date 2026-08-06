using System.Reflection;
using CCInfoWindows.Services.Interfaces;
using CCInfoWindows.ViewModels;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.UI.Xaml;

namespace CCInfoWindows.Tests.Convention;

/// <summary>
/// Phase 24 DISPATCH-06: enforces convention G-1 (CLAUDE.md MVVM Conventions).
///
/// G-1: every IRecipient&lt;T&gt;.Receive(T) body that mutates [ObservableProperty] fields,
/// calls INavigationService, or touches XAML controls MUST wrap the body in
/// IDispatcherQueue.TryEnqueue. Always-TryEnqueue, no HasThreadAccess shortcut
/// (PITFALLS C2-P1).
///
/// Exemption: a method may be marked [ThreadSafeReceive("reason")] with a non-empty
/// reason. Per D-02, the attribute constructor itself enforces non-empty reason —
/// this test additionally asserts the attribute is reachable via reflection.
///
/// Scope (CD-05 #3 option b): Window subclasses are excluded from the body-scan
/// rule because they are by-construction UI-thread-bound. Window receivers must
/// still carry [ThreadSafeReceive(reason)] to document the exemption explicitly.
///
/// Mechanism (D-03 minimal-cost variant): IL-bytecode scan of method body looking
/// for a call/callvirt opcode whose resolved member is IDispatcherQueue.TryEnqueue.
/// Source-generator artifacts (e.g. CommunityToolkit messaging glue) are NOT
/// IRecipient&lt;T&gt; implementations — they are filtered out by the IRecipient
/// interface check.
/// </summary>
public class MessengerThreadingConventionTests
{
    [Fact]
    public void All_IRecipient_Receive_Methods_Either_Marshal_Or_Are_ThreadSafeAttributed()
    {
        var assembly = typeof(MainViewModel).Assembly;

        var receivers = EnumerateReceiverMethods(assembly).ToList();

        Assert.True(receivers.Count >= 3,
            $"Expected at least 3 IRecipient<> Receive methods; found {receivers.Count}. " +
            "Inventory after finding 37 removed the three settings channels that could never have a " +
            "live recipient: MainViewModel.Receive(AuthStateChangedMessage), " +
            "MainWindow.Receive(ThemeChangedMessage), MainWindow.Receive(ResetWindowSizeMessage).");

        var violations = new List<string>();
        foreach (var (method, declaringType) in receivers)
        {
            var attr = method.GetCustomAttribute<ThreadSafeReceiveAttribute>();
            if (attr != null)
            {
                if (string.IsNullOrWhiteSpace(attr.Reason))
                {
                    violations.Add(
                        $"{declaringType.FullName}.{FormatSignature(method)} carries [ThreadSafeReceive] without a non-empty reason. " +
                        "Provide a justification string per D-02.");
                }
                continue;
            }

            if (declaringType.IsSubclassOf(typeof(Window)))
            {
                violations.Add(
                    $"{declaringType.FullName}.{FormatSignature(method)} is a Window receiver but lacks [ThreadSafeReceive(reason)]. " +
                    "CD-05 #3: Window subclasses MUST carry an explicit exemption attribute.");
                continue;
            }

            if (!BodyCallsTryEnqueue(method))
            {
                violations.Add(
                    $"{declaringType.FullName}.{FormatSignature(method)} mutates UI state without IDispatcherQueue.TryEnqueue " +
                    "and lacks [ThreadSafeReceive(reason)]. See CLAUDE.md G-1.");
            }
        }

        Assert.True(
            violations.Count == 0,
            "G-1 convention violations found:\n  - " + string.Join("\n  - ", violations));
    }

    [Fact]
    public void ThreadSafeReceiveAttribute_RejectsEmptyReason_AtConstruction()
    {
        // D-02 spot check: attribute itself enforces non-empty reason.
        // (Belt-and-suspenders — the All_... test above also catches whitespace reasons via reflection,
        // but if a developer somehow tries to bypass the constructor at runtime this test catches it.)
        Assert.Throws<ArgumentException>(() => new ThreadSafeReceiveAttribute(""));
        Assert.Throws<ArgumentException>(() => new ThreadSafeReceiveAttribute("   "));
        var ok = new ThreadSafeReceiveAttribute("documented reason");
        Assert.Equal("documented reason", ok.Reason);
    }

    // -- helpers --

    private static IEnumerable<(MethodInfo Method, Type DeclaringType)> EnumerateReceiverMethods(Assembly assembly)
    {
        foreach (var type in assembly.GetTypes())
        {
            if (type.IsAbstract || type.IsInterface) continue;

            var recipientInterfaces = type.GetInterfaces()
                .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IRecipient<>));

            foreach (var iface in recipientInterfaces)
            {
                var messageType = iface.GetGenericArguments()[0];
                var method = type.GetMethod(
                    nameof(IRecipient<object>.Receive),
                    BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly,
                    binder: null,
                    types: [messageType],
                    modifiers: null);

                if (method != null)
                {
                    yield return (method, type);
                }
            }
        }
    }

    private static string FormatSignature(MethodInfo method)
    {
        var paramType = method.GetParameters()[0].ParameterType.Name;
        return $"Receive({paramType})";
    }

    /// <summary>
    /// IL-bytecode scan: walks the method body looking for a call/callvirt
    /// opcode whose 4-byte metadata token resolves to a MethodInfo declared on
    /// IDispatcherQueue (or a type implementing it) with name "TryEnqueue".
    ///
    /// Note: a positive match here only proves the call exists somewhere in the
    /// method body, not that the entire body is wrapped. G-1 is intent-driven —
    /// reviewer + author are responsible for full-body wrapping; this test catches
    /// the common case where TryEnqueue is missing entirely.
    /// </summary>
    private static bool BodyCallsTryEnqueue(MethodInfo method)
    {
        var body = method.GetMethodBody();
        if (body == null) return false;
        var il = body.GetILAsByteArray();
        if (il == null || il.Length == 0) return false;

        var module = method.Module;
        var genericMethodArgs = method.IsGenericMethod ? method.GetGenericArguments() : null;
        var genericTypeArgs = method.DeclaringType?.IsGenericType == true
            ? method.DeclaringType.GetGenericArguments()
            : null;

        for (int i = 0; i + 4 < il.Length; i++)
        {
            var opcode = il[i];
            // 0x28 = call, 0x6F = callvirt
            if (opcode != 0x28 && opcode != 0x6F) continue;

            int token = il[i + 1] | (il[i + 2] << 8) | (il[i + 3] << 16) | (il[i + 4] << 24);
            MethodBase? resolved;
            try
            {
                resolved = module.ResolveMethod(token, genericTypeArgs, genericMethodArgs);
            }
            catch
            {
                continue;   // tokens for varargs / non-method members; ignore
            }

            if (resolved is MethodInfo mi
                && mi.Name == nameof(IDispatcherQueue.TryEnqueue)
                && (mi.DeclaringType == typeof(IDispatcherQueue)
                    || (mi.DeclaringType?.GetInterfaces().Contains(typeof(IDispatcherQueue)) ?? false)))
            {
                return true;
            }
        }
        return false;
    }
}
