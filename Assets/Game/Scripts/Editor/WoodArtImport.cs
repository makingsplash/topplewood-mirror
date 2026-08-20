using UnityEditor;
using UnityEngine;

namespace Game.Editor
{
    /// <summary>
    /// Настройки импорта деревянного арта — кодом, а не руками в инспекторе.
    /// Причина: спрайты приходят от владельца пачкой и переливаются заново, а руками выставленные
    /// границы 9-slice и PPU при перезаливке теряются молча. Здесь они объявлены один раз.
    ///
    /// Границы (L,B,R,T) считаны с самих картинок: у рамки деревянный кант ~46 px, у полки
    /// скруглённые торцы ~60 px и светлая кромка сверху/снизу ~40 px. Кнопки НЕ режем —
    /// в них запечён текст, растягивать его нельзя.
    /// </summary>
    public static class WoodArtImport
    {
        private const float PixelsPerUnit = 100f;

        private struct Item
        {
            public string Path;
            public Vector4 Border;
            public bool Compress;
        }

        private static readonly Item[] Items =
        {
            new Item { Path = "Assets/Resources/Wood/wood_background.png", Border = Vector4.zero, Compress = true },
            // Полку режем ТОЛЬКО по горизонтали. Вертикальные границы (40/40 на картинке высотой 207)
            // при полке в 0.9 единицы оставляли середине почти ноль: светлая кромка и тёмный низ
            // занимали всю высоту, и доска выглядела раздавленной.
            new Item { Path = "Assets/Resources/Wood/wood_shelf.png", Border = new Vector4(60, 0, 60, 0) },
            new Item { Path = "Assets/Resources/Wood/wood_frame.png", Border = new Vector4(46, 46, 46, 46) },
            new Item { Path = "Assets/Resources/Wood/wood_button_dev.png", Border = Vector4.zero },
            new Item { Path = "Assets/Resources/Wood/wood_button_restart.png", Border = Vector4.zero },
            // Угловая листва. Не режем: это цельный рисунок, 9-slice его порвёт. Жмём — картинки
            // большие (2.4k), а мягкие градиенты на листьях блочное сжатие переживают спокойно,
            // в отличие от текста кнопок.
            new Item { Path = "Assets/Resources/Wood/wood_leaves_left.png", Border = Vector4.zero, Compress = true },
            new Item { Path = "Assets/Resources/Wood/wood_leaves_right.png", Border = Vector4.zero, Compress = true },
            // Три кадра вентилятора. Не режем и НЕ жмём: кадры мелкие (456×252), а блочное сжатие
            // на мягком градиенте лопастей даёт заметные ступеньки.
            new Item { Path = "Assets/Resources/Wood/wood_fan_1.png", Border = Vector4.zero },
            new Item { Path = "Assets/Resources/Wood/wood_fan_2.png", Border = Vector4.zero },
            new Item { Path = "Assets/Resources/Wood/wood_fan_3.png", Border = Vector4.zero },
        };

        [MenuItem("Tools/Wood Style/Переимпортировать арт")]
        public static void Apply()
        {
            var changed = 0;
            foreach (var item in Items)
            {
                if (AssetImporter.GetAtPath(item.Path) is not TextureImporter importer)
                {
                    Debug.LogWarning($"[wood] нет ассета {item.Path} — пропускаю");
                    continue;
                }

                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.spritePixelsPerUnit = PixelsPerUnit;
                importer.spriteBorder = item.Border;
                importer.alphaIsTransparency = true;
                importer.mipmapEnabled = false;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.filterMode = FilterMode.Bilinear;
                importer.maxTextureSize = 2048;
                // Фон — большой, его жмём; мелкие элементы держим без компрессии: артефакты
                // блочного сжатия на канте рамки и на тексте кнопок видны сразу.
                importer.textureCompression = item.Compress
                    ? TextureImporterCompression.CompressedHQ
                    : TextureImporterCompression.Uncompressed;

                // Full Rect обязателен для 9-slice: у Tight-меша спрайт обрезан по альфе, и режим
                // Sliced у SpriteRenderer/Image на нём молча не работает (полка и рамка тянутся).
                var settings = new TextureImporterSettings();
                importer.ReadTextureSettings(settings);
                settings.spriteMeshType = SpriteMeshType.FullRect;
                importer.SetTextureSettings(settings);

                importer.SaveAndReimport();
                changed++;
            }

            Debug.Log($"[wood] импорт арта настроен: {changed} из {Items.Length}");
        }
    }
}
