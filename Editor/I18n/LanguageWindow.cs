using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace KF.GitUI
{
    /// <summary>
    /// 界面语言窗口（M4 多语言贡献框架）：列出可用语言包与**覆盖率**，一键切换，
    /// 并能导出"语言骨架"给贡献者填空。
    ///
    /// 与 ignore 模板窗口同构，但语言包是键级单元：允许部分翻译（未提供的键回退英文），
    /// 覆盖率与"被丢弃的键"（占位符不一致）都明示给用户，不做静默兜底。
    /// </summary>
    public sealed class LanguageWindow : EditorWindow
    {
        private sealed class Choice
        {
            public string Lang;          // null = 英文基线
            public string Label;
            public LanguagePack Pack;    // null = 英文基线
        }

        private List<Choice> choices = new List<Choice>();
        private int selectedIndex;
        private string status = string.Empty;
        private bool statusIsError;

        private ListView listView;
        private Label detailLabel;
        private Label statusLabel;

        public static void Open()
        {
            var w = GetWindow<LanguageWindow>(false, I18n.L(I18n.Keys.LanguageTitle));
            w.minSize = new Vector2(660, 380);
            w.Reload();
            w.Show();
        }

        private void OnEnable()
        {
            Reload();
        }

        private void Reload()
        {
            try
            {
                choices = new List<Choice>
                {
                    new Choice
                    {
                        Lang = null,
                        Label = I18n.L(I18n.Keys.LanguageEnglish),
                        Pack = null
                    }
                };

                foreach (var pack in LanguagePackLibrary.LoadAll())
                    choices.Add(new Choice { Lang = pack.Lang, Label = pack.DisplayName, Pack = pack });

                // 选中项跟随当前语言（英文基线时选第一项）
                selectedIndex = 0;
                var current = I18n.CurrentLanguage;
                if (!string.IsNullOrEmpty(current))
                {
                    for (var i = 0; i < choices.Count; i++)
                    {
                        if (choices[i].Lang == current) { selectedIndex = i; break; }
                    }
                }

                status = string.Empty;
                statusIsError = false;
            }
            catch (Exception ex)
            {
                choices = new List<Choice>();
                status = I18n.L(I18n.Keys.LanguageError, ex.Message);
                statusIsError = true;
            }

            BuildUI();
        }

        private Choice Selected
        {
            get
            {
                if (selectedIndex < 0 || selectedIndex >= choices.Count) return null;
                return choices[selectedIndex];
            }
        }

        private void BuildUI()
        {
            rootVisualElement.Clear();

            var hint = new Label(I18n.L(I18n.Keys.LanguageHint));
            hint.style.whiteSpace = WhiteSpace.Normal;
            hint.style.paddingTop = 4;
            hint.style.paddingLeft = 6;
            hint.style.paddingRight = 6;
            rootVisualElement.Add(hint);

            var dirs = new Label(I18n.L(I18n.Keys.IgnoreTemplatesDirectories,
                LanguagePackLibrary.BuiltInDirectory(),
                LanguagePackLibrary.ProjectSharedDirName));
            dirs.style.paddingLeft = 6;
            dirs.style.paddingBottom = 4;
            dirs.style.fontSize = 10;
            dirs.style.whiteSpace = WhiteSpace.Normal;
            rootVisualElement.Add(dirs);

            var split = new TwoPaneSplitView(0, 260, TwoPaneSplitViewOrientation.Horizontal);
            split.style.flexGrow = 1f;

            var listPane = new VisualElement();
            listPane.style.flexDirection = FlexDirection.Column;
            var listTitle = new Label(I18n.L(I18n.Keys.LanguageList));
            listTitle.style.unityFontStyleAndWeight = FontStyle.Bold;
            listTitle.style.paddingLeft = 4;
            listTitle.style.paddingTop = 2;
            listPane.Add(listTitle);

            listView = new ListView(choices, 20, () => new Label(), (el, i) =>
            {
                var label = (Label)el;
                var choice = choices[i];
                label.text = choice.Pack == null
                    ? choice.Label
                    : choice.Label + "  ·  " + choice.Pack.CoverageText;
                label.style.paddingLeft = 4;
                label.tooltip = choice.Pack == null ? null : choice.Pack.FilePath;
            });
            if (selectedIndex < choices.Count) listView.selectedIndex = selectedIndex;
            listView.selectionChanged += items =>
            {
                foreach (var item in items)
                {
                    var choice = item as Choice;
                    if (choice == null) continue;
                    selectedIndex = choices.IndexOf(choice);
                    break;
                }
                RefreshDetail();
            };
            listView.style.flexGrow = 1f;
            listPane.Add(listView);

            if (choices.Count <= 1 && string.IsNullOrEmpty(status))
                listPane.Add(new Label(I18n.L(I18n.Keys.LanguageNone)));

            split.Add(listPane);

            var detailPane = new VisualElement();
            detailPane.style.flexDirection = FlexDirection.Column;
            detailLabel = new Label();
            detailLabel.style.whiteSpace = WhiteSpace.Normal;
            detailLabel.style.paddingTop = 4;
            detailLabel.style.paddingLeft = 6;
            detailLabel.style.paddingRight = 6;
            detailPane.Add(detailLabel);
            split.Add(detailPane);

            rootVisualElement.Add(split);

            rootVisualElement.Add(BuildBottomBar());

            statusLabel = new Label(status);
            statusLabel.style.paddingLeft = 6;
            statusLabel.style.paddingBottom = 4;
            statusLabel.style.whiteSpace = WhiteSpace.Normal;
            if (statusIsError) statusLabel.style.color = new Color(0.85f, 0.30f, 0.30f, 1f);
            rootVisualElement.Add(statusLabel);

            RefreshDetail();
        }

        private VisualElement BuildBottomBar()
        {
            var bar = new VisualElement();
            bar.name = "language-actions";
            bar.style.flexDirection = FlexDirection.Row;
            bar.style.alignItems = Align.Center;
            bar.style.paddingTop = 2;
            bar.style.paddingLeft = 4;
            bar.style.paddingRight = 4;

            var applyBtn = new Button(DoApply) { text = I18n.L(I18n.Keys.LanguageApply) };
            applyBtn.name = "btn-language-apply";
            bar.Add(applyBtn);

            var exportBtn = new Button(DoExportSkeleton) { text = I18n.L(I18n.Keys.LanguageExport) };
            exportBtn.name = "btn-language-export";
            bar.Add(exportBtn);

            var folderBtn = new Button(OpenProjectFolder) { text = I18n.L(I18n.Keys.LanguageOpenFolder) };
            folderBtn.name = "btn-language-folder";
            bar.Add(folderBtn);

            var rescanBtn = new Button(Reload) { text = I18n.L(I18n.Keys.LanguageRescan) };
            rescanBtn.name = "btn-language-rescan";
            bar.Add(rescanBtn);

            return bar;
        }

        private void RefreshDetail()
        {
            if (detailLabel == null) return;

            var choice = Selected;
            if (choice == null)
            {
                detailLabel.text = I18n.L(I18n.Keys.LanguageNone);
                return;
            }

            if (choice.Pack == null)
            {
                detailLabel.text = I18n.L(I18n.Keys.LanguageEnglish) + "\n"
                    + I18n.L(I18n.Keys.LanguageCoverage, I18n.KeyCount, I18n.KeyCount) + "\n"
                    + I18n.L(I18n.Keys.LanguageFallbackNote);
                return;
            }

            var pack = choice.Pack;
            var text = choice.Label + "\n"
                + I18n.L(I18n.Keys.LanguageCoverage, pack.Coverage, I18n.KeyCount) + "\n"
                + I18n.L(I18n.Keys.LanguageFallbackNote);

            if (pack.Maintainers != null && pack.Maintainers.Count > 0)
                text += "\n" + I18n.L(I18n.Keys.LanguageMaintainers, string.Join(", ", pack.Maintainers.ToArray()));
            if (pack.UnknownKeys.Count > 0)
                text += "\n" + I18n.L(I18n.Keys.LanguageUnknownKeys, pack.UnknownKeys.Count);
            if (pack.InvalidKeys.Count > 0)
                text += "\n" + I18n.L(I18n.Keys.LanguageInvalidKeys, pack.InvalidKeys.Count);
            if (!string.IsNullOrEmpty(pack.Error))
                text += "\n" + I18n.L(I18n.Keys.LanguageError, pack.Error);
            if (!string.IsNullOrEmpty(pack.FilePath))
                text += "\n" + pack.FilePath;

            detailLabel.text = text;
        }

        private void DoApply()
        {
            var choice = Selected;
            if (choice == null) return;

            try
            {
                I18n.SetLanguage(choice.Lang);
                status = choice.Lang == null
                    ? I18n.L(I18n.Keys.LanguageAppliedEnglish)
                    : I18n.L(I18n.Keys.LanguageApplied, choice.Lang);
                statusIsError = false;
            }
            catch (Exception ex)
            {
                status = I18n.L(I18n.Keys.LanguageError, ex.Message);
                statusIsError = true;
            }

            Reload(); // 换语言后本窗口自身的文案也要重建
        }

        private void DoExportSkeleton()
        {
            var suggested = "zh-CN";
            var choice = Selected;
            if (choice != null && !string.IsNullOrEmpty(choice.Lang)) suggested = choice.Lang;

            var lang = PromptDialog.Show(
                I18n.L(I18n.Keys.LanguageExport),
                I18n.L(I18n.Keys.LanguageExportPrompt),
                suggested);
            if (lang == null) return;

            string packPath;
            string metaPath;
            string error;
            if (LanguagePackLibrary.ExportSkeleton(lang.Trim(), LanguagePackLibrary.ProjectSharedDirectory(),
                    out packPath, out metaPath, out error))
            {
                status = I18n.L(I18n.Keys.LanguageExportDone, packPath);
                statusIsError = false;
                AssetDatabase.Refresh();
            }
            else
            {
                status = error;
                statusIsError = true;
            }

            Reload();
        }

        private void OpenProjectFolder()
        {
            try
            {
                var dir = LanguagePackLibrary.ProjectSharedDirectory();
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                EditorUtility.RevealInFinder(dir);
            }
            catch (Exception ex)
            {
                status = I18n.L(I18n.Keys.LanguageError, ex.Message);
                statusIsError = true;
                if (statusLabel != null) statusLabel.text = status;
            }
        }
    }
}
