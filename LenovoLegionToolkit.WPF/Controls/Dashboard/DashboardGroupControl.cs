using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using LenovoLegionToolkit.WPF.Extensions;

namespace LenovoLegionToolkit.WPF.Controls.Dashboard;

public class DashboardGroupControl : UserControl
{
    private readonly DashboardGroup _dashboardGroup;

    public DashboardGroupControl(DashboardGroup dashboardGroup)
    {
        _dashboardGroup = dashboardGroup;

        Initialized += DashboardGroupControl_Initialized;
    }

    private async void DashboardGroupControl_Initialized(object? sender, System.EventArgs e)
    {
        // PERFORMANCE FIX: Show structure immediately, populate controls asynchronously on UI thread
        var stackPanel = new StackPanel { Margin = new(0, 0, 16, 0) };

        var textBlock = new TextBlock
        {
            Text = _dashboardGroup.GetName(),
            Focusable = true,
            FontSize = 24,
            FontWeight = FontWeights.Medium,
            Margin = new(0, 16, 0, 24)
        };
        AutomationProperties.SetName(textBlock, textBlock.Text);
        stackPanel.Children.Add(textBlock);

        // CRITICAL: Set content immediately to show title - don't wait for controls
        Content = stackPanel;

        // Create controls asynchronously on UI thread (controls MUST be created on UI thread in WPF)
        var controlsTasks = _dashboardGroup.Items.Select(i => i.GetControlAsync());
        var controls = await Task.WhenAll(controlsTasks);

        // Add controls to UI (already on UI thread, so this is safe)
        foreach (var control in controls.SelectMany(c => c))
        {
            stackPanel.Children.Add(control);
        }
    }
}
