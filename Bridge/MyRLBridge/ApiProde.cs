using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Godot;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Models;

namespace MyRLBridge.MyRLBridgeCode;

public static class ApiProbe
{
    public static void DumpAll()
    {
        var sb = new StringBuilder();

        sb.AppendLine("========== ACTION API DIAGNOSTIC ==========");
        sb.AppendLine($"Captured: {DateTime.Now:O}");

        DumpType(typeof(CardModel), sb);
        DumpType(typeof(PotionModel), sb);

        var assembly = typeof(CombatManager).Assembly;

        // Discover action types without guessing their namespaces.
        var types = assembly.GetTypes()
            .Where(type =>
                type.Name == "RunManager" ||
                type.Name == "ActionQueueSynchronizer" ||
                type.Name == "PlayCardAction" ||
                type.Name == "EndPlayerTurnAction" ||
                (
                    type.Name.Contains(
                        "Potion",
                        StringComparison.OrdinalIgnoreCase) &&
                    type.Name.EndsWith(
                        "Action",
                        StringComparison.Ordinal)
                ))
            .OrderBy(type => type.FullName);

        foreach (var type in types)
        {
            DumpType(type, sb);
        }

        var directory =
            @"C:\Users\super\Documents\MyRLBridge";

        Directory.CreateDirectory(directory);

        var path = Path.Combine(
            directory,
            "sts2_action_api_dump.txt");

        File.WriteAllText(path, sb.ToString());

        GD.Print($"Action API diagnostic written to: {path}");
    }

    private static void DumpType(Type type, StringBuilder sb)
    {
        const BindingFlags flags =
            BindingFlags.Public |
            BindingFlags.Instance |
            BindingFlags.Static;

        sb.AppendLine();
        sb.AppendLine("================================");
        sb.AppendLine($"TYPE: {type.FullName}");
        sb.AppendLine("================================");

        sb.AppendLine("--- PUBLIC PROPERTIES ---");

        foreach (var property in type.GetProperties(flags))
        {
            var accessor =
                property.GetMethod ?? property.SetMethod;

            var prefix = accessor?.IsStatic == true
                ? "static "
                : "";

            sb.AppendLine(
                $"{prefix}{TypeName(property.PropertyType)} " +
                $"{property.Name}");
        }

        sb.AppendLine("--- PUBLIC FIELDS ---");

        foreach (var field in type.GetFields(flags))
        {
            var prefix = field.IsStatic ? "static " : "";

            sb.AppendLine(
                $"{prefix}{TypeName(field.FieldType)} " +
                $"{field.Name}");
        }

        sb.AppendLine("--- CONSTRUCTORS ---");

        foreach (var constructor in type.GetConstructors())
        {
            sb.AppendLine(
                $"{type.Name}(" +
                $"{FormatParameters(constructor.GetParameters())})");
        }

        sb.AppendLine("--- PUBLIC METHODS ---");

        foreach (var method in type.GetMethods(flags))
        {
            if (method.IsSpecialName ||
                method.DeclaringType == typeof(object))
            {
                continue;
            }

            // Exclude the large inherited AbstractModel hook list.
            if (method.DeclaringType == typeof(AbstractModel))
            {
                continue;
            }

            var prefix = method.IsStatic ? "static " : "";

            sb.AppendLine(
                $"{prefix}{TypeName(method.ReturnType)} " +
                $"{method.Name}(" +
                $"{FormatParameters(method.GetParameters())})");
        }
    }

    private static string FormatParameters(
        ParameterInfo[] parameters)
    {
        return string.Join(
            ", ",
            parameters.Select(parameter =>
                $"{TypeName(parameter.ParameterType)} " +
                $"{parameter.Name}" +
                (parameter.IsOptional ? " [optional]" : "")));
    }

    private static string TypeName(Type type)
    {
        return type.FullName ?? type.Name;
    }
}