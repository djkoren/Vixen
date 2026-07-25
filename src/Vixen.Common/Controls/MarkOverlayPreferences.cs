namespace Common.Controls
{
	/// <summary>
	/// Shared visibility state for the mark overlays drawn on top of the curve and colour gradient
	/// editors. Both editors expose a "Hide Marks" toggle and both read this, so flipping it in one
	/// applies to every editor opened afterwards. The value is persisted in the application settings
	/// file so the choice survives a restart.
	/// </summary>
	public static class MarkOverlayPreferences
	{
		private const string SettingPath = "MarkOverlay/HideMarks";

		private static bool? _hideMarks;

		/// <summary>
		/// The colour the mark overlays are drawn in. A light yellow reads clearly over both the dark
		/// curve grid and an arbitrary gradient.
		/// </summary>
		public static Color MarkColor { get; } = Color.FromArgb(255, 255, 240, 150);

		/// <summary>
		/// True when mark overlays should be suppressed. Defaults to false (marks shown).
		/// </summary>
		public static bool HideMarks
		{
			get
			{
				if (_hideMarks == null)
				{
					try
					{
						var xml = new XMLProfileSettings();
						_hideMarks = xml.GetSetting(XMLProfileSettings.SettingType.Preferences, SettingPath, false);
					}
					catch
					{
						// A malformed or unreadable settings file must not stop an editor from opening.
						_hideMarks = false;
					}
				}

				return _hideMarks.Value;
			}
			set
			{
				if (_hideMarks == value) return;
				_hideMarks = value;
				try
				{
					var xml = new XMLProfileSettings();
					xml.PutSetting(XMLProfileSettings.SettingType.Preferences, SettingPath, value);
				}
				catch
				{
					// Persisting is best effort; the in-memory value still applies for this session.
				}
			}
		}
	}
}
