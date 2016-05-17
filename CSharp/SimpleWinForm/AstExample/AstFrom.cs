namespace AstExample
{
    using System;
    using System.Collections.Generic;
    using System.Drawing;
    using System.Linq;
    using System.Windows.Forms;

    // Representation of AST's node properties.
    public interface IProperty
    {
        string Name { get; }
        string Value { get; }
        string Type { get; }
    }

    // Representation of AST node.
    public interface INode
    {
        string Name { get; }
        int Offset { get; }
        int Length { get; }
        ICollection<INode> Children { get; }
        ICollection<IProperty> Properties { get; }
    }

    public static class AstForm
    {
        // Default font used in all controls.
        static Font DefaultFont = new Font("Consolas", 12.0f);

        public static void CreateApplication(
            Func<string, IReadOnlyCollection<INode>> parser,
            string initialScript)
        {
            // Initialize Application,
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            // and start the main event loop.
            Application.Run(
                CreateForm(parser, initialScript));
        }

        public static Form CreateForm(
            Func<string, IReadOnlyCollection<INode>> parser,
            string initialScript)
        {
            // ----------------------------------------------------------------
            // Create Data View that shows properties of the selected AST node in table form.
            var dataView = new DataGridView
            {
                Dock = DockStyle.Fill,
                Font = DefaultFont,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                AllowUserToResizeRows = false,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells,
                ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize,
                ColumnHeadersVisible = true,
                ReadOnly = true,
                RowHeadersVisible = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                TabIndex = 2,
                ScrollBars = ScrollBars.Vertical,
            };
            dataView.Columns.AddRange(
                new DataGridViewTextBoxColumn
                {
                    HeaderText = "Property",
                    ReadOnly = true,
                    AutoSizeMode = DataGridViewAutoSizeColumnMode.AllCellsExceptHeader
                },
                new DataGridViewTextBoxColumn
                {
                    HeaderText = "Value",
                    ReadOnly = true,
                    AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
                    Resizable = DataGridViewTriState.True
                },
                new DataGridViewTextBoxColumn
                {
                    HeaderText = "Type",
                    ReadOnly = true,
                    AutoSizeMode = DataGridViewAutoSizeColumnMode.AllCellsExceptHeader
                }
            );
            dataView.KeyUp += HandleChangeFontSize;

            // ----------------------------------------------------------------
            // Create Text Box to show parsed text.
            // If the text box has not been edited, selecting an AST in the tree view
            // will select the matching text in the script view.
            var scriptView = new TextBox
            {
                Dock = DockStyle.Fill,
                Font = DefaultFont,
                HideSelection = false,
                Multiline = true,
                ShortcutsEnabled = true,
                ScrollBars = ScrollBars.Both,
                TabIndex = 1,
                Text = initialScript,
                WordWrap = false,
            };

            // Ctrl-A is not implemented in Text Box control for some reason, so lets add it.
            scriptView.KeyDown += (sender, eArg) =>
            {
                if (eArg.Control && eArg.KeyCode == Keys.A)
                    scriptView.SelectAll();
            };


            // ----------------------------------------------------------------
            // Create the Tree View to be placed into the left panel.
            var treeView = new TreeView
            {
                Dock = DockStyle.Fill,
                Font = DefaultFont,
                TabIndex = 0,
            };
            treeView.AfterSelect +=
                (sender, eArgs) =>
                {
                    // clear old properties
                    dataView.Rows.Clear();

                    // display new properties
                    var selNode = (INode)eArgs.Node.Tag;
                    if (selNode.Properties != null)
                    {
                        foreach (var property in selNode.Properties)
                        {
                            dataView.Rows.Add(property.Name, property.Value, property.Type);
                        }
                    }

                    // update script view selection If the text box has changed,
                    // skip doing anything with it until we've updated the tree view.
                    if (!scriptView.Modified)
                    {
                        scriptView.SelectionStart = selNode.Offset;
                        scriptView.SelectionLength = selNode.Length;
                        scriptView.ScrollToCaret();
                    }
                };
            treeView.KeyUp += HandleChangeFontSize;
            scriptView.KeyUp +=
                (sender, eArgs) =>
                {
                    if (eArgs.KeyCode == Keys.F5 && eArgs.Alt == false &&
                        eArgs.Control == false && eArgs.Shift == false)
                    {
                        // Handle "F5" by reparsing the text, update the tree view,
                        // and reset modified status.
                        eArgs.Handled = true;
                        scriptView.Modified = false;
                        treeView.Nodes.Clear();
                        treeView.Nodes.AddRange(
                            VisitNodes(
                                parser(scriptView.Text)));
                    }
                    else
                    {
                        HandleChangeFontSize(sender, eArgs);
                    }
                };

            // ----------------------------------------------------------------
            // Start from horisontally split container that hosts tree view and properties.
            var horizontal = new SplitContainer
            {
                Orientation = Orientation.Horizontal,
                Font = DefaultFont,
                Dock = DockStyle.Fill,
                SplitterWidth = 10,
            };
            horizontal.Panel1.Controls.Add(treeView);
            horizontal.Panel2.Controls.Add(dataView);

            // Change cursor when dragging splitter
            horizontal.SplitterMoving +=
                (sender, eArgs) => { Cursor.Current = Cursors.NoMoveVert; };
            horizontal.SplitterMoved +=
                (sender, eArgs) => { Cursor.Current = Cursors.Default; };
            // ----------------------------------------------------------------
            // Then vertically split container that hosts the above container and the script view.
            var vertical = new SplitContainer
            {
                Orientation = Orientation.Vertical,
                Font = DefaultFont,
                Dock = DockStyle.Fill,
                SplitterWidth = 10,
            };
            vertical.Panel1.Controls.Add(horizontal);
            vertical.Panel2.Controls.Add(scriptView);

            // Change cursor when dragging splitter
            vertical.SplitterMoving +=
                (sender, eArgs) => { Cursor.Current = Cursors.NoMoveHoriz; };
            vertical.SplitterMoved +=
                (sender, eArgs) => { Cursor.Current = Cursors.Default; };

            // ----------------------------------------------------------------
            // Lastly create top level Form and add vertically split container to it.
            var form = new Form();
            form.Text = "Ast Explorer";

            // Initial form size.
            form.ClientSize = new Size { Width = 1200, Height = 700 };
            form.Controls.Add(vertical);

            treeView.KeyUp += HandleChangeFontSize;

            treeView.Nodes.AddRange(
                VisitNodes(
                    parser(scriptView.Text)));
            return form;
        }

        // Helper handler to change font size of the `sender` control.
        // "Ctrl-+" for larger font up to 60;
        // "Ctrl--" for smaller font down to 2;
        // "Ctrl-0" to restore default font.
        private static void HandleChangeFontSize(object sender, KeyEventArgs eArgs)
        {
            var control = sender as Control;
            if (control != null &&
                eArgs.Alt == false && eArgs.Control == true && eArgs.Shift == false)
            {
                // Handle "Ctrl-+" to increase font size.
                if (eArgs.KeyCode == Keys.Add || eArgs.KeyCode == Keys.Oemplus)
                {
                    eArgs.Handled = true;
                    if (control.Font.Size < 60.0f)
                        control.Font = new Font("Consolas", control.Font.Size + 1.0f);
                }
                // Handle "Ctrl--" to decrease font size.
                else if (eArgs.KeyCode == Keys.Subtract || eArgs.KeyCode == Keys.OemMinus)
                {
                    eArgs.Handled = true;
                    if (control.Font.Size > 2.0f)
                        control.Font = new Font("Consolas", control.Font.Size - 1.0f);
                }
                // Handle "Ctrl-0" to reset font size to default.
                else if (eArgs.KeyCode == Keys.D0)
                {
                    eArgs.Handled = true;
                    control.Font = DefaultFont;
                }
            }
        }

        // Helper function to recursively walk the INode AST tree and convert them
        // into TreeNode representation.
        private static TreeNode VisitNode(INode node)
        {
            var treeNode = new TreeNode
            {
                Text = string.Format("{0} [{1}, {2})", node.Name, node.Offset, node.Length),
                Tag = node,
            };

            if (node.Children != null)
            {
                treeNode.Nodes.AddRange(VisitNodes(node.Children));
            }

            treeNode.Expand();
            return treeNode;
        }

        private static TreeNode[] VisitNodes(IEnumerable<INode> nodes)
        {
            return nodes.Select(VisitNode).ToArray();
        }
    }
}
