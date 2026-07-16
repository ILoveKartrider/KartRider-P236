using System.Runtime.ExceptionServices;
using Xunit;

namespace KartRider.P236.Connector.Tests;

public sealed class MainFormLayoutTests
{
    [Fact]
    public void L1DataPatchControlsAreIsolatedOnTheirOwnTab()
    {
        Exception? failure = null;
        Thread thread = new(() =>
        {
            try
            {
                using MainForm form = new(loadPreparedInstances: false);
                TabControl tabs = Assert.IsType<TabControl>(
                    Assert.Single(form.Controls.Find("mainTabs", searchAllChildren: true)));
                Assert.Equal(
                    ["접속 및 실행", "L1 데이터 패치"],
                    tabs.TabPages.Cast<TabPage>().Select(page => page.Text));

                TabPage launchPage = Assert.IsType<TabPage>(
                    Assert.Single(form.Controls.Find("launchTabPage", searchAllChildren: true)));
                TabPage l1PatchPage = Assert.IsType<TabPage>(
                    Assert.Single(form.Controls.Find("l1PatchTabPage", searchAllChildren: true)));
                Button patchButton = Assert.IsType<Button>(
                    Assert.Single(form.Controls.Find("l1PatchApplyButton", searchAllChildren: true)));
                Button restoreButton = Assert.IsType<Button>(
                    Assert.Single(form.Controls.Find("l1PatchRestoreButton", searchAllChildren: true)));
                Button launchButton = Assert.IsType<Button>(
                    Assert.Single(form.Controls.Find("launchButton", searchAllChildren: true)));
                CheckBox hookCheckBox = Assert.IsType<CheckBox>(
                    Assert.Single(form.Controls.Find(
                        "applyL1CompatibilityHooksCheckBox",
                        searchAllChildren: true)));
                TableLayoutPanel l1PatchLayout = Assert.IsType<TableLayoutPanel>(
                    Assert.Single(form.Controls.Find("l1PatchLayout", searchAllChildren: true)));
                Label description = Assert.IsType<Label>(
                    Assert.Single(form.Controls.Find(
                        "l1PatchDescriptionLabel",
                        searchAllChildren: true)));

                Assert.Equal("L1 패치 적용…", patchButton.Text);
                Assert.Equal("원본 복원", restoreButton.Text);
                Assert.Same(l1PatchPage, FindOwningTabPage(patchButton));
                Assert.Same(l1PatchPage, FindOwningTabPage(restoreButton));
                Assert.Same(launchPage, FindOwningTabPage(launchButton));
                Assert.Same(launchPage, FindOwningTabPage(hookCheckBox));

                Assert.Same(launchButton, form.AcceptButton);
                tabs.SelectedTab = l1PatchPage;
                form.RefreshDefaultButtonForSelectedTab();
                Assert.Null(form.AcceptButton);
                tabs.SelectedTab = launchPage;
                form.RefreshDefaultButtonForSelectedTab();
                Assert.Same(launchButton, form.AcceptButton);

                l1PatchLayout.Dock = DockStyle.None;
                l1PatchLayout.Size = new Size(660, 240);
                l1PatchLayout.PerformLayout();
                int availableDescriptionWidth =
                    l1PatchLayout.ClientSize.Width -
                    l1PatchLayout.Padding.Horizontal -
                    description.Margin.Horizontal;
                Assert.True(availableDescriptionWidth >= 200);
                Assert.Equal(availableDescriptionWidth, description.MaximumSize.Width);

                form.SetBusy(true);
                Assert.False(tabs.Enabled);
                Assert.True(form.ShouldCancelClose(CloseReason.UserClosing));
                Assert.False(form.ShouldCancelClose(CloseReason.WindowsShutDown));
                FormClosingEventArgs busyClose = new(CloseReason.UserClosing, cancel: false);
                form.MainForm_FormClosing(form, busyClose);
                Assert.True(busyClose.Cancel);
                form.SetBusy(false);
                Assert.True(tabs.Enabled);
                Assert.False(form.ShouldCancelClose(CloseReason.UserClosing));
                FormClosingEventArgs idleClose = new(CloseReason.UserClosing, cancel: false);
                form.MainForm_FormClosing(form, idleClose);
                Assert.False(idleClose.Cancel);
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (failure is not null)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }

    private static TabPage? FindOwningTabPage(Control control)
    {
        for (Control? current = control.Parent; current is not null; current = current.Parent)
        {
            if (current is TabPage tabPage)
            {
                return tabPage;
            }
        }

        return null;
    }
}
