using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Iliai.UiBuild
{
    public sealed class UiFill
    {
        public string Sprite;
        public string Color;
        public string ImageType;
        public bool PreserveAspect;
    }

    /// <summary>Смещения узла. Помнит, КАКИЕ ключи автор реально написал — на этом стоит проверка
    /// «edges на точечном анкере» (в схеме это ошибка, а не молчаливый ноль).</summary>
    public sealed class UiOffset
    {
        public bool HasEdges;
        public bool HasPoint;
        public int Left, Right, Top, Bottom;
        public int X, Y;

        public static UiOffset Parse(UiReader parent, string key)
        {
            UiReader r = parent.Sub(key, parent.Where + "." + key);
            if (r == null) return null;
            var o = new UiOffset();
            if (r.Has("left") || r.Has("right") || r.Has("top") || r.Has("bottom")) o.HasEdges = true;
            if (r.Has("x") || r.Has("y")) o.HasPoint = true;
            o.Left = r.Int("left", 0);
            o.Right = r.Int("right", 0);
            o.Top = r.Int("top", 0);
            o.Bottom = r.Int("bottom", 0);
            o.X = r.Int("x", 0);
            o.Y = r.Int("y", 0);
            r.Finish();
            return o;
        }
    }

    public sealed class UiLayoutSpec
    {
        public string Dir = "vertical";
        public int Spacing;
        public RectOffset Padding = new RectOffset();
        public string Align = "upper-center";
        public bool ControlSize = true;
        public bool ForceExpand;
        public Vector2Int Cell = new Vector2Int(100, 100);
        public int ConstraintCount;

        public static UiLayoutSpec Parse(UiReader parent, string key)
        {
            UiReader r = parent.Sub(key, parent.Where + "." + key);
            if (r == null) return null;
            var l = new UiLayoutSpec
            {
                Dir = r.Str("dir", "vertical"),
                Spacing = r.Int("spacing", 0),
                Align = r.Str("align", "upper-center"),
                ControlSize = r.Bool("controlSize", true),
                ForceExpand = r.Bool("forceExpand", false),
                ConstraintCount = r.Int("constraintCount", 0),
            };
            Vector2Int? cell = r.SizeOpt("cell");
            if (cell.HasValue) l.Cell = cell.Value;
            UiReader pad = r.Sub("padding", r.Where + ".padding");
            if (pad != null)
            {
                l.Padding = new RectOffset(pad.Int("left", 0), pad.Int("right", 0), pad.Int("top", 0), pad.Int("bottom", 0));
                pad.Finish();
            }
            r.Finish();
            return l;
        }
    }

    /// <summary>Блок поведения: `type` + произвольные параметры, которые читает хэндлер этого типа.</summary>
    public sealed class UiBehaviourSpec
    {
        public string Type;
        public Dictionary<string, object> Raw;
        public string Where;

        /// <summary>Читатель параметров блока для хэндлера. `type` уже вычтен, поэтому Finish()
        /// поймает именно опечатки в параметрах, а не служебное поле.</summary>
        public UiReader Reader(UiBuildReport report)
        {
            var r = new UiReader(Raw, report, Where);
            r.RawValue("type");
            return r;
        }

        public static List<UiBehaviourSpec> ParseList(UiReader parent, string key, UiBuildReport report)
        {
            List<object> list = parent.List(key);
            if (list == null) return null;
            var result = new List<UiBehaviourSpec>();
            for (int i = 0; i < list.Count; i++)
            {
                if (!(list[i] is Dictionary<string, object> map))
                {
                    report.Error(parent.Where, $"behaviour[{i}] должен быть объектом вида {{ \"type\": \"...\" }}");
                    continue;
                }
                if (!map.TryGetValue("type", out object t) || !(t is string type) || string.IsNullOrEmpty(type))
                {
                    report.Error(parent.Where, $"behaviour[{i}] без 'type'");
                    continue;
                }
                result.Add(new UiBehaviourSpec { Type = type, Raw = map, Where = parent.Where + $".behaviour[{type}]" });
            }
            return result;
        }
    }

    public sealed class UiNode
    {
        public string Id;
        public string Type = "node";
        public string Prefab;
        public string Anchor;
        public UiOffset Offset;
        public Vector2Int? Size;
        public int? Width, Height;
        public Vector2? Pivot;
        public float Rotation;

        public string Sprite;
        public string Color;
        public string ImageType;
        public bool PreserveAspect;

        public string Text;
        public string Style;
        public string Align;
        public bool Wrap;
        public int MaxLines;

        public UiLayoutSpec Layout;
        public string Fit;                       // null | "both" | "w" | "h"
        public float FlexibleWidth = -1f;
        public float FlexibleHeight = -1f;
        public bool IgnoreLayout;
        public bool Hidden;
        public bool DisableRaycast;

        public string Bind;
        public List<UiBehaviourSpec> Behaviour;
        public List<UiNode> Children;
        public string Where;
    }

    public sealed class UiSpec
    {
        public string Name;
        public string Kind = "screen";
        public string Controller;
        public Vector2Int? Size;
        public Vector2 Pivot = new Vector2(0.5f, 0.5f);
        public UiFill Background;
        public List<UiBehaviourSpec> Behaviour;
        public List<UiNode> Children;

        public string SourcePath;                 // относительный путь для сообщений отчёта
        public string SpriteFolder;               // папка спрайтов этой спеки (рядом с ней)

        public static UiSpec Load(string absolutePath, string relativePathForMessages, UiBuildReport report)
        {
            string text;
            try { text = File.ReadAllText(absolutePath); }
            catch (IOException e)
            {
                report.Error(relativePathForMessages, "не прочитан: " + e.Message);
                return null;
            }

            object root;
            try { root = UiJson.Parse(text); }
            catch (UiJsonException e)
            {
                report.Error(relativePathForMessages, "битый JSON: " + e.Message);
                return null;
            }

            if (!(root is Dictionary<string, object> map))
            {
                report.Error(relativePathForMessages, "корень спеки должен быть объектом");
                return null;
            }

            var r = new UiReader(map, report, relativePathForMessages);
            var spec = new UiSpec
            {
                SourcePath = relativePathForMessages,
                Name = r.Str("name"),
                Kind = r.Str("kind", "screen"),
                Controller = r.Str("controller"),
                Size = r.SizeOpt("size"),
            };
            Vector2? pivot = r.Vec2Opt("pivot");
            if (pivot.HasValue) spec.Pivot = pivot.Value;

            UiReader bg = r.Sub("background", relativePathForMessages + ".background");
            if (bg != null)
            {
                spec.Background = new UiFill
                {
                    Sprite = bg.Str("sprite"),
                    Color = bg.Str("color"),
                    ImageType = bg.Str("imageType"),
                    PreserveAspect = bg.Bool("preserveAspect"),
                };
                bg.Finish();
            }

            spec.Behaviour = UiBehaviourSpec.ParseList(r, "behaviour", report);
            spec.Children = ParseChildren(r, "children", relativePathForMessages, report);
            r.Finish();

            if (string.IsNullOrEmpty(spec.Name))
                report.Error(relativePathForMessages, "нет обязательного 'name'");
            if (spec.Kind != "screen" && spec.Kind != "component")
                report.Error(relativePathForMessages, $"'kind' = '{spec.Kind}', допустимо screen | component");

            return spec;
        }

        static List<UiNode> ParseChildren(UiReader parent, string key, string where, UiBuildReport report)
        {
            List<object> list = parent.List(key);
            if (list == null) return null;
            var nodes = new List<UiNode>();
            var seenIds = new HashSet<string>();
            for (int i = 0; i < list.Count; i++)
            {
                if (!(list[i] is Dictionary<string, object> map))
                {
                    report.Error(where, $"children[{i}] должен быть объектом");
                    continue;
                }
                UiNode node = ParseNode(map, where, report);
                if (node == null) continue;
                if (!seenIds.Add(node.Id))
                    report.Error(node.Where, "дублирующийся id среди сиблингов — имена детей обязаны быть уникальными (по ним идёт bind и deep-override)");
                nodes.Add(node);
            }
            return nodes;
        }

        static UiNode ParseNode(Dictionary<string, object> map, string parentWhere, UiBuildReport report)
        {
            string id = map.TryGetValue("id", out object rawId) && rawId is string s ? s : null;
            string where = parentWhere + ":" + (id ?? "<без id>");
            var r = new UiReader(map, report, where);
            var node = new UiNode
            {
                Where = where,
                Id = r.Str("id"),
                Type = r.Str("type", "node"),
                Prefab = r.Str("prefab"),
                Anchor = r.Str("anchor"),
                Size = r.SizeOpt("size"),
                Width = r.IntOpt("width"),
                Height = r.IntOpt("height"),
                Rotation = r.Float("rotation", 0f),
                Sprite = r.Str("sprite"),
                Color = r.Str("color"),
                ImageType = r.Str("imageType"),
                PreserveAspect = r.Bool("preserveAspect"),
                Text = r.Str("text"),
                Style = r.Str("style"),
                Align = r.Str("align"),
                Wrap = r.Bool("wrap"),
                MaxLines = r.Int("maxLines", 0),
                FlexibleWidth = r.Float("flexibleWidth", -1f),
                FlexibleHeight = r.Float("flexibleHeight", -1f),
                IgnoreLayout = r.Bool("ignoreLayout"),
                Hidden = r.Bool("hidden"),
                DisableRaycast = r.Bool("disableRaycast"),
                Bind = r.Str("bind"),
            };
            node.Pivot = r.Vec2Opt("pivot");
            node.Offset = UiOffset.Parse(r, "offset");
            node.Layout = UiLayoutSpec.Parse(r, "layout");
            node.Fit = ParseFit(r, report, where);
            node.Behaviour = UiBehaviourSpec.ParseList(r, "behaviour", report);
            node.Children = ParseChildren(r, "children", where, report);
            r.Finish();

            if (string.IsNullOrEmpty(node.Id))
                report.Error(where, "у ребёнка нет обязательного 'id'");
            if (node.Type != "node" && node.Type != "image" && node.Type != "text")
                report.Error(where, $"'type' = '{node.Type}', допустимо node | image | text");
            return node;
        }

        /// <summary>`fit` — единственное поле схемы с двумя допустимыми типами (bool либо "w"/"h"),
        /// поэтому читается сырым значением: типизированный геттер дал бы ложную ошибку на легальном вводе.</summary>
        static string ParseFit(UiReader r, UiBuildReport report, string where)
        {
            if (!r.Has("fit")) return null;
            object raw = r.RawValue("fit");
            if (raw is bool b) return b ? "both" : null;
            if (raw is string s)
            {
                if (s == "w" || s == "h" || s == "both") return s;
                report.Error(where, $"'fit' = '{s}', допустимо true | false | \"w\" | \"h\"");
                return null;
            }
            report.Error(where, "'fit' — это true/false или \"w\"/\"h\"");
            return null;
        }
    }
}
