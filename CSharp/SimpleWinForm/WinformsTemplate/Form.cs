namespace WinformsTemplate
{
    using System;
    using System.Drawing;
    using System.Linq;
    using System.Windows.Forms;

    static class Program
    {
        [STAThread]
        static void Main()
        {
            CreateApplication();
        }

        static void CreateApplication()
        {
            // Initialize Application
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            // Default font used in all controls
            var font = new Font("Consolas", 12.0f);

            // ----------------------------------------------------------------
            // Create sample Text Box to be placed into the right panel.
            var textView = new TextBox
            {
                Dock = DockStyle.Fill,
                Font = font,
                HideSelection = false,
                Multiline = true,
                ShortcutsEnabled = true,
                ScrollBars = ScrollBars.Both,
                TabIndex = 1,
                Text = LOREM,
                WordWrap = false,
                // Make sure user cannot change content, since there we don't handle
                // effect of such change in the interactions across controls.
                ReadOnly = true,
            };
            
            // Ctrl-A is not implemented in Text Box control for some reason, so lets add it.
            textView.KeyDown += (sender, eArg) =>
            {
                if (eArg.Control && eArg.KeyCode == Keys.A)
                    textView.SelectAll();
            };

            // ----------------------------------------------------------------
            // Status Label to be placed at the bottom of the Form.
            var statusLabel = new Label
            {
                Dock = DockStyle.Fill,
                Font = font,
                Text = "Status",
                Height = font.Height + 20, // font height + 10px margins
            };

            // ----------------------------------------------------------------
            // Create the Tree View to be placed into the left panel.
            var treeView = new TreeView
            {
                Dock = DockStyle.Fill,
                Font = font,
                TabIndex = 0,
            };
            treeView.AfterSelect +=
                (sender, eArgs) =>
                {
                    var obj = (Tuple<int, int>)eArgs.Node.Tag;
                    statusLabel.Text = textView.Text.Substring(obj.Item1, obj.Item2);
                    textView.SelectionStart = obj.Item1;
                    textView.SelectionLength = obj.Item2;
                    textView.ScrollToCaret();
                };

            // ----------------------------------------------------------------
            // Start from vertically split container that hosts tree view and text box.
            var vertical = new SplitContainer
            {
                Orientation = Orientation.Vertical,
                Font = font,
                Dock = DockStyle.Fill,
                SplitterWidth = 10,
                TabIndex = 2,
            };
            vertical.Panel1.Controls.Add(treeView);
            vertical.Panel2.Controls.Add(textView);

            // Change cursor when dragging splitter
            vertical.SplitterMoving += (sender, eArgs) => { Cursor.Current = Cursors.NoMoveHoriz; };
            vertical.SplitterMoved += (sender, eArgs) => { Cursor.Current = Cursors.Default; };

            // ----------------------------------------------------------------
            // Now horizontally split container to host status label at the bottom panel, and
            // the rest of the controls at the top panel.
            var horizontal = new SplitContainer
            {
                Orientation = Orientation.Horizontal,
                Font = font,
                Dock = DockStyle.Fill,
                SplitterWidth = 10,
                FixedPanel = FixedPanel.Panel2, // Force Status bar to not resize
                IsSplitterFixed = true,
            };
            horizontal.Panel1.Controls.Add(vertical);
            horizontal.Panel2.Controls.Add(statusLabel);

            // Change cursor when dragging splitter
            horizontal.SplitterMoving += (sender, eArgs) => { Cursor.Current = Cursors.NoMoveVert; };
            horizontal.SplitterMoved += (sender, eArgs) => { Cursor.Current = Cursors.Default; };

            // ----------------------------------------------------------------
            // Lastly create top level Form and add horizontally split container to it.
            var form = new Form();
            form.Text = "Winforms template";

            // Initial form size.
            form.ClientSize = new Size { Width = 800, Height = 600 };
            form.Controls.Add(horizontal);

            TextToTree(treeView, textView.Text);

            // Finally, start the main event loop of our application.
            Application.Run(form);
        }

        // Convert text into lines and create TreeNode for each line, where
        // TreeNode text is the first word of the line, and data (i.e. tag) contains
        // start position and length in the text box control.
        private static void TextToTree(TreeView treeView, string text)
        {
            var pos = 0;
            foreach (var line in text.Split('\n'))
            {
                var firstWord = line.Split(' ').FirstOrDefault();
                var isWordEmpty = string.IsNullOrWhiteSpace(firstWord);
                var lineLength = line.Length + 1; // +1 accounts for '\r' at the end :)
                treeView.Nodes.Add(new TreeNode
                {
                    Text = isWordEmpty ? "<empty>" : firstWord,
                    Tag = Tuple.Create(pos, isWordEmpty ? 0 : lineLength),
                });

                pos += lineLength;
            };
        }

        private static string LOREM =
            "Lorem ipsum dolor sit amet, consectetur adipiscing elit.\r\n" +
            "Aenean tempus consectetur lorem nec finibus.\r\n" +
            "Sed aliquam in enim venenatis semper.\r\n" +
            "Sed iaculis in tellus in posuere.\r\n" +
            "Suspendisse potenti.\r\n" +
            "Donec turpis libero, finibus ut erat nec, bibendum fermentum quam.\r\n" +
            "Pellentesque libero odio, rhoncus vel neque sed, lobortis ullamcorper nunc.\r\n" +
            "Maecenas lectus est, feugiat id ex laoreet, molestie venenatis ligula.\r\n" +
            "In aliquet sollicitudin blandit.\r\n" +
            "Nunc vulputate dolor in nibh suscipit ultricies.\r\n" +
            "Mauris malesuada porttitor ante, sodales accumsan libero tincidunt et.\r\n" +
            "Nunc malesuada quam at justo aliquet scelerisque.\r\n" +
            "In commodo, ex eget accumsan tincidunt, nisl massa convallis sapien, in ultrices mi massa et nunc.\r\n" +
            "Donec vestibulum, dui ut porttitor feugiat, erat urna volutpat metus, vitae faucibus urna velit ut ante.\r\n" +
            "Curabitur at vulputate dui, quis posuere ligula.\r\n" +
            "In a elementum libero.\r\n" +
            "Mauris et leo id lorem hendrerit eleifend a vitae elit.\r\n" +
            "\r\n" +
            "Interdum et malesuada fames ac ante ipsum primis in faucibus.\r\n" +
            "Curabitur fringilla laoreet purus, sed varius nibh ullamcorper at.\r\n" +
            "Curabitur imperdiet augue nec quam dapibus, ut vestibulum nunc feugiat.\r\n" +
            "Vivamus egestas, nunc ut volutpat bibendum, velit neque tempor nulla, non egestas felis felis a massa.\r\n" +
            "Morbi in blandit elit.\r\n" +
            "Nunc sagittis tempor accumsan.\r\n" +
            "Vivamus id dapibus est, id laoreet tellus.\r\n" +
            "Vestibulum congue auctor neque, a egestas lacus aliquam vitae.\r\n" +
            "Phasellus pellentesque risus at odio laoreet, ut mattis dolor gravida.\r\n" +
            "Cras accumsan elementum turpis, non rutrum metus molestie et.\r\n" +
            "Duis facilisis, neque pulvinar hendrerit tempus, turpis neque ornare enim, et consequat est arcu consectetur ligula.\r\n" +
            "Proin sed vulputate magna.\r\n";
    }
}
