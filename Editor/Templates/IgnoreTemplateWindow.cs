using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace KF.GitUI
{
    /// <summary>
    /// 一键 ignore 模板窗口（M4 支柱四）：左列模板（内置 / 项目 / 本地三类来源），右侧为
    /// <b>写入结果预览</b>（对当前项目 .gitignore 真实计算），底部选写入模式并落盘。
    ///
    /// 默认模式是**合并**（只追加缺失规则、保留既有内容），覆盖模式先备份为 .gitignore.bak。
    /// 另提供"把当前 .gitignore 导出为项目共享模板"，让贡献者把现成清单变成可提 PR 的模板。
    /// </summary>
    public sealed class IgnoreTemplateWindow : EditorWindow
    {
        private List<GitIgnoreTemplate> templates = new List<GitIgnoreTemplate>();
        private int selectedIndex = -1;
        private bool overwriteMode;
        private string status = string.Empty;
        private bool statusIsError;

        private ListView listView;
        private TextField previewField;
        private Label summaryLabel;
        private Label statusLabel;
        private Label directoriesLabel;

        public static void Open()
        {
            var w = GetWindow<IgnoreTemplateWindow>(false, I18n.L(I18n.Keys.IgnoreTemplatesTitle));
            w.minSize = new Vector2(680, 420);
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
                templates = GitIgnoreTemplateLibrary.LoadAll();
                status = string.Empty;
                statusIsError = false;
            }
            catch (Exception ex)
            {
                templates = new List<GitIgnoreTemplate>();
                status = I18n.L(I18n.Keys.IgnoreTemplatesError, ex.Message);
                statusIsError = true;
            }

            if (selectedIndex >= templates.Count) selectedIndex = templates.Count - 1;
            BuildUI();
            if (selectedIndex < 0 && templates.Count > 0) selectedIndex = 0;
            if (listView != null && selectedIndex >= 0) listView.selectedIndex = selectedIndex;
            RefreshPreview();
        }

        private GitIgnoreTemplate Selected
        {
            get
            {
                if (selectedIndex < 0 || selectedIndex >= templates.Count) return null;
                return templates[selectedIndex];
            }
        }

        private void BuildUI()
        {
            rootVisualElement.Clear();

            var hint = new Label(I18n.L(I18n.Keys.IgnoreTemplatesHint));
            hint.style.whiteSpace = WhiteSpace.Normal;
            hint.style.paddingTop = 4;
            hint.style.paddingLeft = 6;
            hint.style.paddingRight = 6;
            rootVisualElement.Add(hint);

            directoriesLabel = new Label();
            directoriesLabel.style.paddingLeft = 6;
            directoriesLabel.style.paddingBottom = 4;
            directoriesLabel.style.fontSize = 10;
            directoriesLabel.style.whiteSpace = WhiteSpace.Normal;
            directoriesLabel.text = I18n.L(I18n.Keys.IgnoreTemplatesDirectories,
                GitIgnoreTemplateLibrary.BuiltInDirectory(),
                GitIgnoreTemplateLibrary.ProjectSharedDirName);
            rootVisualElement.Add(directoriesLabel);

            var split = new TwoPaneSplitView(0, 240, TwoPaneSplitViewOrientation.Horizontal);
            split.style.flexGrow = 1f;

            // 左：模板列表（显示名 + 来源）
            listView = new ListView(templates, 20, () => new Label(), (el, i) =>
            {
                var label = (Label)el;
                var t = templates[i];
                label.text = t.DisplayName + "  ·  " + t.SourceLabel;
                label.style.paddingLeft = 4;
                label.tooltip = string.IsNullOrEmpty(t.Description)
                    ? t.Id + "  (" + t.RuleCount + ")"
                    : t.Description + "\n" + t.Id + "  (" + t.RuleCount + ")";
            });
            listView.selectionChanged += items =>
            {
                foreach (var item in items)
                {
                    var t = item as GitIgnoreTemplate;
                    if (t == null) continue;
                    selectedIndex = templates.IndexOf(t);
                    break;
                }
                RefreshPreview();
            };
            var listPane = new VisualElement();
            listPane.name = "templates-list-pane";
            listPane.style.flexDirection = FlexDirection.Column;
            var listTitle = new Label(I18n.L(I18n.Keys.IgnoreTemplatesList));
            listTitle.style.unityFontStyleAndWeight = FontStyle.Bold;
            listTitle.style.paddingLeft = 4;
            listTitle.style.paddingTop = 2;
            listPane.Add(listTitle);
            listView.style.flexGrow = 1f;
            listPane.Add(listView);
            split.Add(listPane);

            // 右：结果预览
            var previewPane = new VisualElement();
            previewPane.name = "templates-preview-pane";
            previewPane.style.flexDirection = FlexDirection.Column;

            summaryLabel = new Label();
            summaryLabel.style.paddingLeft = 4;
            summaryLabel.style.paddingTop = 2;
            summaryLabel.style.paddingBottom = 2;
            summaryLabel.style.whiteSpace = WhiteSpace.Normal;
            previewPane.Add(summaryLabel);

            previewField = new TextField(I18n.L(I18n.Keys.IgnoreTemplatesPreview))
            {
                multiline = true,
                isReadOnly = true
            };
            previewField.name = "templates-preview";
            previewField.style.flexGrow = 1f;
            previewField.style.whiteSpace = WhiteSpace.Normal;
            previewPane.Add(previewField);
            split.Add(previewPane);

            rootVisualElement.Add(split);

            rootVisualElement.Add(BuildBottomBar());

            statusLabel = new Label(status);
            statusLabel.style.paddingLeft = 6;
            statusLabel.style.paddingBottom = 4;
            statusLabel.style.whiteSpace = WhiteSpace.Normal;
            if (statusIsError) statusLabel.style.color = new Color(0.85f, 0.30f, 0.30f, 1f);
            rootVisualElement.Add(statusLabel);
        }

        private VisualElement BuildBottomBar()
        {
            var bar = new VisualElement();
            bar.name = "templates-actions";
            bar.style.flexDirection = FlexDirection.Row;
            bar.style.alignItems = Align.Center;
            bar.style.paddingTop = 2;
            bar.style.paddingLeft = 4;
            bar.style.paddingRight = 4;

            var modes = new List<string>
            {
                I18n.L(I18n.Keys.IgnoreTemplatesModeMerge),
                I18n.L(I18n.Keys.IgnoreTemplatesModeOverwrite)
            };
            var modePopup = new PopupField<string>(I18n.L(I18n.Keys.IgnoreTemplatesMode), modes,
                overwriteMode ? 1 : 0);
            modePopup.name = "templates-mode";
            modePopup.style.minWidth = 260;
            modePopup.RegisterValueChangedCallback(evt =>
            {
                overwriteMode = evt.newValue == modes[1];
                RefreshPreview();
            });
            bar.Add(modePopup);

            var writeBtn = new Button(DoWrite) { text = I18n.L(I18n.Keys.IgnoreTemplatesWrite) };
            writeBtn.name = "btn-templates-write";
            bar.Add(writeBtn);

            var exportBtn = new Button(DoExport) { text = I18n.L(I18n.Keys.IgnoreTemplatesExport) };
            exportBtn.name = "btn-templates-export";
            bar.Add(exportBtn);

            var folderBtn = new Button(OpenProjectSharedFolder) { text = I18n.L(I18n.Keys.IgnoreTemplatesOpenFolder) };
            folderBtn.name = "btn-templates-folder";
            bar.Add(folderBtn);

            var rescanBtn = new Button(Reload) { text = I18n.L(I18n.Keys.IgnoreTemplatesRescan) };
            rescanBtn.name = "btn-templates-rescan";
            bar.Add(rescanBtn);

            return bar;
        }

        /// <summary>对当前项目 .gitignore 真实计算写入结果（不落盘）。</summary>
        private void RefreshPreview()
        {
            if (previewField == null || summaryLabel == null) return;

            var t = Selected;
            if (t == null)
            {
                previewField.value = string.Empty;
                summaryLabel.text = I18n.L(I18n.Keys.IgnoreTemplatesEmpty);
                return;
            }

            try
            {
                var path = GitIgnoreTemplateLibrary.GitIgnorePath();
                var existing = File.Exists(path) ? File.ReadAllText(path) : null;
                var plan = overwriteMode
                    ? GitIgnoreWriter.Overwrite(existing, t.Content)
                    : GitIgnoreWriter.Merge(existing, t.Content);

                previewField.value = plan.Result;

                if (plan.Created)
                    summaryLabel.text = I18n.L(I18n.Keys.IgnoreTemplatesWillCreate, t.RuleCount);
                else if (overwriteMode)
                    summaryLabel.text = I18n.L(I18n.Keys.IgnoreTemplatesWillOverwrite, t.RuleCount);
                else if (plan.NothingToAdd)
                    summaryLabel.text = I18n.L(I18n.Keys.IgnoreTemplatesUpToDate, t.DisplayName, plan.SkippedRules);
                else
                    summaryLabel.text = I18n.L(I18n.Keys.IgnoreTemplatesSummary, plan.AddedRules, plan.SkippedRules);
            }
            catch (Exception ex)
            {
                previewField.value = string.Empty;
                summaryLabel.text = I18n.L(I18n.Keys.IgnoreTemplatesError, ex.Message);
            }

            if (statusLabel != null) statusLabel.text = status;
        }

        private void DoWrite()
        {
            var t = Selected;
            if (t == null) return;

            try
            {
                var path = GitIgnoreTemplateLibrary.GitIgnorePath();
                var result = GitIgnoreWriter.Write(path, t.Content, overwriteMode);

                if (result.Overwritten)
                    status = I18n.L(I18n.Keys.IgnoreTemplatesOverwritten, t.DisplayName, result.AddedRules, result.BackupPath);
                else if (result.Created)
                    status = I18n.L(I18n.Keys.IgnoreTemplatesCreated, t.DisplayName, result.AddedRules);
                else if (result.NothingToAdd)
                    status = I18n.L(I18n.Keys.IgnoreTemplatesUpToDate, t.DisplayName, result.SkippedRules);
                else
                    status = I18n.L(I18n.Keys.IgnoreTemplatesMerged, t.DisplayName, result.AddedRules, result.SkippedRules);

                statusIsError = false;
                AssetDatabase.Refresh();
            }
            catch (Exception ex)
            {
                status = I18n.L(I18n.Keys.IgnoreTemplatesError, ex.Message);
                statusIsError = true;
                Debug.LogError("[gitui] ignore template write failed: " + ex);
            }

            BuildUI();
            RefreshPreview();
            Repaint();
        }

        private void DoExport()
        {
            var suggested = Selected != null && GitIgnoreTemplate.IsValidId(Selected.Id)
                ? Selected.Id
                : "project-ignore";

            var id = PromptDialog.Show(
                I18n.L(I18n.Keys.IgnoreTemplatesExport),
                I18n.L(I18n.Keys.IgnoreTemplatesExportPrompt),
                suggested);
            if (id == null) return; // 取消

            id = id.Trim();
            string exported;
            string error;
            if (GitIgnoreTemplateLibrary.ExportProjectGitIgnore(id, out exported, out error))
            {
                status = I18n.L(I18n.Keys.IgnoreTemplatesExported, exported);
                statusIsError = false;
                Debug.Log("[gitui] " + status);
            }
            else
            {
                status = error;
                statusIsError = true;
            }

            BuildUI();
            RefreshPreview();
            Repaint();
        }

        private void OpenProjectSharedFolder()
        {
            try
            {
                var dir = GitIgnoreTemplateLibrary.ProjectSharedDirectory();
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                EditorUtility.RevealInFinder(dir);
            }
            catch (Exception ex)
            {
                status = I18n.L(I18n.Keys.IgnoreTemplatesError, ex.Message);
                statusIsError = true;
                if (statusLabel != null) statusLabel.text = status;
            }
        }
    }
}
