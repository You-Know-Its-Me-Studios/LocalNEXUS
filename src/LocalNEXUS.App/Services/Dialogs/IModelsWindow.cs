namespace LocalNEXUS.App.Services.Dialogs;

/// <summary>
/// Shows the window for finding and downloading a model.
/// </summary>
/// <remarks>
/// Behind a service for the same reason the file pickers are: a view model that reached for a
/// Window would be holding a window handle, and nothing else in this application does.
/// </remarks>
public interface IModelsWindow
{
    /// <summary>
    /// Opens the window, or brings it forward when it is already open.
    /// </summary>
    /// <param name="viewModel">The model browser view model the window binds to.</param>
    void Show(object viewModel);

    /// <summary>Closes it if it is open. Called when the application shuts down.</summary>
    void Close();
}
