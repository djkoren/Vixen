using Common.Controls;
using Common.Resources.Properties;
using Vixen.Module.App;
using Vixen.Services;
using ZedGraph;
using Common.Controls.Theme;
using NCalc2;

namespace VixenModules.App.Curves
{
	public partial class CurveEditor : BaseForm
	{
		private double _previousCurveYLocation;
		private double _tempX;
		private bool _drawCurve;
		private string _holdFunction = String.Empty;

		// Bezier handle dragging state
		private bool _isDraggingHandle;
		private int _dragHandlePointIndex = -1;
		private bool _dragHandleIsIn; // true = in-handle, false = out-handle

		/// <summary>
		/// Optional waveform peak data. Each element is a peak amplitude [0..1].
		/// The array spans the effect's full duration, mapped to X range 0-100.
		/// Null means no audio available.
		/// </summary>
		public float[] WaveformPeaks { get; set; }

		/// <summary>
		/// Optional mark positions within the effect, expressed as X positions (0-100).
		/// Each entry includes position and display color.
		/// </summary>
		public List<(double Position, Color Color)> MarkPositions { get; set; }

		public CurveEditor()
		{
			InitializeComponent();
			ThemeUpdateControls.UpdateControls(this);

			textBoxThreshold.MaxLength = 2;

			zedGraphControl.GraphPane.XAxis.MajorGrid.IsVisible = true;
			zedGraphControl.GraphPane.XAxis.MajorGrid.Color = ThemeColorTable.GroupBoxBorderColor;
			zedGraphControl.GraphPane.XAxis.MajorGrid.DashOff = 4;
			zedGraphControl.GraphPane.XAxis.MajorGrid.DashOn = 2;

			zedGraphControl.GraphPane.YAxis.MajorGrid.IsVisible = true;
			zedGraphControl.GraphPane.YAxis.MajorGrid.Color = ThemeColorTable.GroupBoxBorderColor;
			zedGraphControl.GraphPane.YAxis.MajorGrid.DashOff = 4;
			zedGraphControl.GraphPane.YAxis.MajorGrid.DashOn = 2;

			zedGraphControl.EditModifierKeys = Keys.None;
			zedGraphControl.IsShowContextMenu = false;
			zedGraphControl.IsEnableSelection = false;
			zedGraphControl.EditButtons = MouseButtons.Left;
			zedGraphControl.GraphPane.XAxis.Scale.Min = 0;
			zedGraphControl.GraphPane.XAxis.Scale.Max = 100;
			zedGraphControl.GraphPane.YAxis.Scale.Min = 0;
			zedGraphControl.GraphPane.YAxis.Scale.Max = 100;
			zedGraphControl.GraphPane.XAxis.Title.IsVisible = false;
			zedGraphControl.GraphPane.YAxis.Title.IsVisible = false;
			zedGraphControl.GraphPane.Legend.IsVisible = false;
			zedGraphControl.GraphPane.Title.IsVisible = false;

			zedGraphControl.GraphPane.Fill = new Fill(ThemeColorTable.BackgroundColor);
			zedGraphControl.GraphPane.Border = new Border(ThemeColorTable.BackgroundColor, 0);

			zedGraphControl.GraphPane.AxisChange();

			// Add waveform/marks when the dialog is shown (after properties are set by caller)
			this.Shown += (s, ev) => AddWaveformAndMarksToGraph();

			// Subscribe to direct MouseClick for Bezier toggle (more reliable than ZedGraph events)
			zedGraphControl.MouseClick += zedGraphControl_BezierMouseClick;
		}

		public CurveEditor(Curve curve)
			: this()
		{
			Curve = curve;
		}

		private Curve _curve;

		public Curve Curve
		{
			get { return _curve; }
			set
			{
				_curve = new Curve(value);
				PopulateFormWithCurve(_curve);
			}
		}

		private string _libraryCurveName;

		public string LibraryCurveName
		{
			get { return _libraryCurveName; }
			set
			{
				_libraryCurveName = value;
				PopulateFormWithCurve(Curve);
			}
		}

		private CurveLibrary _library;

		private CurveLibrary Library
		{
			get
			{
				if (_library == null)
					_library = ApplicationServices.Get<IAppModuleInstance>(CurveLibraryDescriptor.ModuleID) as CurveLibrary;

				return _library;
			}
		}

		public bool ReadonlyCurve { get; internal set; }

		private bool zedGraphControl_PreMouseMoveEvent(ZedGraphControl sender, MouseEventArgs e)
		{
			// Handle Bezier handle grip dragging
			if (_isDraggingHandle && e.Button == MouseButtons.Left)
			{
				double handleX, handleY;
				zedGraphControl.GraphPane.ReverseTransform(e.Location, out handleX, out handleY);

				handleX = Math.Max(0, Math.Min(100, handleX));
				handleY = Math.Max(0, Math.Min(100, handleY));

				PointPairList pointList = zedGraphControl.GraphPane.CurveList[0].Points as PointPairList;
				if (pointList != null && _dragHandlePointIndex >= 0 && _dragHandlePointIndex < pointList.Count)
				{
					var point = pointList[_dragHandlePointIndex];
					var handleData = point.Tag as BezierHandleData;
					if (handleData != null)
					{
						if (_dragHandleIsIn)
						{
							handleData.InHandleX = handleX - point.X;
							handleData.InHandleY = handleY - point.Y;
							// Enforce: in-handle must point left (X offset <= 0)
							if (handleData.InHandleX > 0) handleData.InHandleX = 0;
						}
						else
						{
							handleData.OutHandleX = handleX - point.X;
							handleData.OutHandleY = handleY - point.Y;
							// Enforce: out-handle must point right (X offset >= 0)
							if (handleData.OutHandleX < 0) handleData.OutHandleX = 0;
						}
						UpdateHandleGraphObjects();
					}
				}
				return true; // consume the event
			}

			double newX, newY;
			if (e.Button == MouseButtons.Left && _drawCurve)
			{
				int textThreshold = textBoxThreshold.Text == "" ? (int) 0 : Convert.ToInt16(textBoxThreshold.Text);
				// only add if we've actually clicked on the pane, so make sure the mouse is over it first
				if (zedGraphControl.MasterPane.FindPane(e.Location) != null)
				{
					PointPairList pointList = zedGraphControl.GraphPane.CurveList[0].Points as PointPairList;
					zedGraphControl.GraphPane.ReverseTransform(e.Location, out newX, out newY);
					if (pointList.Count == 0 && _tempX < newX - 1)
					{
						pointList.Insert(0, 0, newY);
					}
					//Verify the point is in the usable bounds.
					if (newX > 100)
					{
						newX = 100;
					}
					else if (newX < 0)
					{
						newX = 0;
					}
					if (newY > 100)
					{
						newY = 100;
					}
					else if (newY < 0)
					{
						newY = 0;
					}

					if (_tempX < newX - textThreshold)
					{
						if (newX >= 0)
							pointList.Insert(0, newX, newY);
						pointList.Sort();
						zedGraphControl.Invalidate();
						_tempX = newX;
					}
				}
			}

			if (zedGraphControl.IsEditing) {
				PointPairList pointList = zedGraphControl.GraphPane.CurveList[0].Points as PointPairList;
				pointList.Sort();
				return false;
			}

			//Used to move Curve higher or lower on the grid or when shift is pressed will flatten curve and then move higher or lower on the grid.
			if (e.Button == MouseButtons.Left && !_drawCurve)
			{
				zedGraphControl.GraphPane.ReverseTransform(e.Location, out newX, out newY);
				if (ModifierKeys.HasFlag(Keys.Shift))
				{
					//Verify the point is in the usable bounds. only care about the Y axis.
					if (newY > 100)
					{
						newY = 100;
					}
					else if (newY < 0)
					{
						newY = 0;
					}
					var points = new PointPairList(new[] {0.0, 100.0}, new[] {newY, newY});

					PointPairList pointList = zedGraphControl.GraphPane.CurveList[0].Points as PointPairList;
					pointList.Clear();
					pointList.Add(points);
					zedGraphControl.Invalidate();
					txtYValue.Text = newY.ToString("0.####");
					txtXValue.Text = "";
				}
				else
				{
					if (ModifierKeys == Keys.None)
					{
						//Move curve higher or lower on the Y axis.
						bool stopUpdating = false;

						PointPairList pointList = zedGraphControl.GraphPane.CurveList[0].Points as PointPairList;

						//Check if any curve point extends past the Y axis.
						foreach (var points in pointList)
						{
							if ((points.Y >= 100 && newY > _previousCurveYLocation) || (points.Y <= 0 && newY < _previousCurveYLocation))
							{
								double adjustedPoint = 0;
								if (points.Y > 100)
									adjustedPoint = points.Y - 100;
								if (points.Y < 0)
									adjustedPoint = points.Y;
								//ensures the curve remains bound by the upper and lower limits.
								foreach (var updatePoints in pointList)
								{
									updatePoints.Y = updatePoints.Y - adjustedPoint;
								}
								stopUpdating = true;
								break;
							}
						}
						if (!stopUpdating)
						{
							//New curve location
							foreach (var points in pointList)
							{
								points.Y = points.Y + (newY - _previousCurveYLocation);
							}
						}
						_previousCurveYLocation = newY;
						zedGraphControl.Invalidate();
					}
				}
			}
			return false;
		}

		private bool zedGraphControl_PostMouseMoveEvent(ZedGraphControl sender, MouseEventArgs e)
		{
			if (sender.DragEditingPair == null)
				return true;

			if (sender.DragEditingPair.X < 0)
				sender.DragEditingPair.X = 0;
			if (sender.DragEditingPair.X > 100)
				sender.DragEditingPair.X = 100;
			if (sender.DragEditingPair.Y < 0)
				sender.DragEditingPair.Y = 0;
			if (sender.DragEditingPair.Y > 100)
				sender.DragEditingPair.Y = 100;

			if (!Curve.IsLibraryReference && e.Button == MouseButtons.Left && !ModifierKeys.HasFlag(Keys.Shift))
			{
				txtXValue.Text = sender.DragEditingPair.X.ToString("0.####");
				txtYValue.Text = sender.DragEditingPair.Y.ToString("0.####");
				txtXValue.Enabled = txtYValue.Enabled = btnUpdateCoordinates.Enabled = true;
			}

			// Update Bezier handle visuals when points are dragged
			PointPairList pointList = zedGraphControl.GraphPane.CurveList[0].Points as PointPairList;
			if (pointList != null && pointList.HasBezierData())
			{
				UpdateHandleGraphObjects();
			}

			return true;
		}

		private bool zedGraphControl_MouseDownEvent(ZedGraphControl sender, MouseEventArgs e)
		{
			if (ReadonlyCurve)
				return false;

			CurveItem curve;
			int dragPointIndex;

			// Bezier handle toggle is handled in zedGraphControl_BezierMouseClick (MouseClick event)
			// to avoid double-firing with MouseDown

			// Handle grip hit-test: check if clicking near a Bezier handle grip
			if (e.Button == MouseButtons.Left && Control.ModifierKeys == Keys.None)
			{
				int handlePointIdx;
				bool isInHandle;
				if (FindNearestHandle(e.Location, out handlePointIdx, out isInHandle))
				{
					_isDraggingHandle = true;
					_dragHandlePointIndex = handlePointIdx;
					_dragHandleIsIn = isInHandle;
					return true; // consume event to prevent ZedGraph point drag
				}
			}

			// if CTRL is pressed, and we're not near a specific point, add a new point

			double newX, newY;
			if (Control.ModifierKeys.HasFlag(Keys.Control) &&
			    !zedGraphControl.GraphPane.FindNearestPoint(e.Location, out curve, out dragPointIndex)) {
				// only add if we've actually clicked on the pane, so make sure the mouse is over it first
				if (zedGraphControl.MasterPane.FindPane(e.Location) != null) {
					PointPairList pointList = zedGraphControl.GraphPane.CurveList[0].Points as PointPairList;
					zedGraphControl.GraphPane.ReverseTransform(e.Location, out newX, out newY);
					//Verify the point is in the usable bounds.
					if (newX > 100)
					{
						newX = 100;
					}
					else if(newX < 0)
					{
						newX = 0;
					}
					if (newY > 100)
					{
						newY = 100;
					}
					else if (newY < 0)
					{
						newY = 0;
					}
					pointList.Insert(0, newX, newY);
					pointList.Sort();
					zedGraphControl.Invalidate();
				}
			}
			// if the ALT key was pressed, and we're near a point, delete it -- but only if there would be at least two points left
			if (Control.ModifierKeys.HasFlag(Keys.Alt) &&
			    zedGraphControl.GraphPane.FindNearestPoint(e.Location, out curve, out dragPointIndex)) {
				PointPairList pointList = zedGraphControl.GraphPane.CurveList[0].Points as PointPairList;
				if (pointList.Count > 2) {
					pointList.RemoveAt(dragPointIndex);
					pointList.Sort();
					zedGraphControl.Invalidate();
				}
			}

			zedGraphControl.GraphPane.ReverseTransform(e.Location, out newX, out newY);
			_previousCurveYLocation = newY;

			if (!Curve.IsLibraryReference && e.Button == MouseButtons.Left && sender.DragEditingPair != null && !ModifierKeys.HasFlag(Keys.Shift))
			{
				txtXValue.Text = sender.DragEditingPair.X.ToString("0.####");
				txtYValue.Text = sender.DragEditingPair.Y.ToString("0.####");
				txtXValue.Enabled = txtYValue.Enabled = btnUpdateCoordinates.Enabled = true;
			}

			return false;
		}

		private bool zedGraphControl_MouseUpEvent(ZedGraphControl sender, MouseEventArgs e)
		{
			if (_isDraggingHandle)
			{
				_isDraggingHandle = false;
				_dragHandlePointIndex = -1;
				UpdateHandleGraphObjects();
				return false;
			}

			if (_drawCurve)
			{
				double newX, newY;
				zedGraphControl.GraphPane.ReverseTransform(e.Location, out newX, out newY);
				PointPairList pointList = zedGraphControl.GraphPane.CurveList[0].Points as PointPairList;
				pointList.Insert(0, 100, newY);
				pointList.Sort();
				zedGraphControl.Invalidate();
				_drawCurve = false;
			}
			
			return false;
		}

		private void PopulateFormWithCurve(Curve curve)
		{
			zedGraphControl.GraphPane.Chart.Fill = new Fill(ThemeColorTable.BackgroundColor);
			zedGraphControl.GraphPane.Chart.Border.Color = ThemeColorTable.GroupBoxBorderColor;
			zedGraphControl.GraphPane.XAxis.MajorGrid.Color = ThemeColorTable.GroupBoxBorderColor;
			zedGraphControl.GraphPane.XAxis.MinorGrid.Color = ThemeColorTable.GroupBoxBorderColor;
			zedGraphControl.GraphPane.XAxis.MajorTic.Color = ThemeColorTable.GroupBoxBorderColor;
			zedGraphControl.GraphPane.XAxis.MinorTic.Color = ThemeColorTable.GroupBoxBorderColor;
			zedGraphControl.GraphPane.YAxis.MajorGrid.Color = ThemeColorTable.GroupBoxBorderColor;
			zedGraphControl.GraphPane.YAxis.MinorGrid.Color = ThemeColorTable.GroupBoxBorderColor;
			zedGraphControl.GraphPane.YAxis.MajorTic.Color = ThemeColorTable.GroupBoxBorderColor;
			zedGraphControl.GraphPane.YAxis.MinorTic.Color = ThemeColorTable.GroupBoxBorderColor;
			zedGraphControl.GraphPane.Title.FontSpec.FontColor = ThemeColorTable.ForeColor;
			zedGraphControl.GraphPane.XAxis.Scale.FontSpec.FontColor = ThemeColorTable.ForeColor;
			zedGraphControl.GraphPane.YAxis.Scale.FontSpec.FontColor = ThemeColorTable.ForeColor;

			// if we're editing a curve from the library, treat it special
			if (curve.IsCurrentLibraryCurve) {
				zedGraphControl.GraphPane.CurveList.Clear();
				zedGraphControl.DragEditingPair = null;
				zedGraphControl.GraphPane.AddCurve(string.Empty, curve.Points, Curve.ActiveCurveGridColor);
				if (LibraryCurveName == null) {
					labelCurve.Text = "This curve is a library curve.";
					Text = "Curve Editor: Library Curve";
				}
				else {
					labelCurve.Text = "This curve is the library curve: " + LibraryCurveName;
					Text = "Curve Editor: Library Curve " + LibraryCurveName;
				}

				zedGraphControl.IsEnableHEdit = true;
				zedGraphControl.IsEnableVEdit = true;
				ReadonlyCurve = false;
				buttonSaveCurveToLibrary.Enabled = false;
				buttonLoadCurveFromLibrary.Enabled = false;
				buttonUnlinkCurve.Enabled = false;
				buttonEditLibraryCurve.Enabled = false;
				btnInvert.Enabled = true;
				btnReverse.Enabled = true;
				labelInstructions1.Visible = true;
				labelInstructions2.Visible = true;
				txtXValue.Enabled = false;
				txtYValue.Enabled = false;
				txtXValue.Text = string.Empty;
				txtYValue.Text = string.Empty;
				btnUpdateCoordinates.Enabled = false;
				
			}
			else {
				if (curve.IsLibraryReference) {
					zedGraphControl.GraphPane.CurveList.Clear();
					LineItem item = zedGraphControl.GraphPane.AddCurve(string.Empty, curve.Points, Curve.InactiveCurveGridColor);
					item.Symbol.Fill = new Fill(Curve.InactiveCurveGridColor);
					labelCurve.Text = "This curve is linked to the library curve: " + curve.LibraryReferenceName;
				}
				else {
					zedGraphControl.GraphPane.CurveList.Clear();
					LineItem item = zedGraphControl.GraphPane.AddCurve(string.Empty, curve.Points, Curve.ActiveCurveGridColor);
					item.Symbol.Fill = new Fill(Curve.ActiveCurveGridColor);
					labelCurve.Text = "This curve is not linked to any in the library.";
				}
				zedGraphControl.DragEditingPair = null;
				zedGraphControl.IsEnableHEdit = !curve.IsLibraryReference;
				zedGraphControl.IsEnableVEdit = !curve.IsLibraryReference;
				ReadonlyCurve = curve.IsLibraryReference;
				buttonSaveCurveToLibrary.Enabled = !curve.IsLibraryReference;
				buttonLoadCurveFromLibrary.Enabled = true;
				buttonUnlinkCurve.Enabled = curve.IsLibraryReference;
				buttonEditLibraryCurve.Enabled = curve.IsLibraryReference;
				btnInvert.Enabled = !curve.IsLibraryReference;
				btnReverse.Enabled = !curve.IsLibraryReference;
				labelInstructions1.Visible = !curve.IsLibraryReference;
				labelInstructions2.Visible = !curve.IsLibraryReference;
				txtYValue.Enabled = !curve.IsLibraryReference;
				txtXValue.Enabled = !curve.IsLibraryReference;
				txtXValue.Text = string.Empty;
				txtYValue.Text = string.Empty;
				btnUpdateCoordinates.Enabled = false;
				
				Text = "Curve Editor";
			}

			// Add waveform and marks background
			AddWaveformAndMarksToGraph();

			// Restore Bezier handle visuals if the curve has handle data
			UpdateHandleGraphObjects();

			zedGraphControl.Invalidate();
		}

		private void buttonLoadCurveFromLibrary_Click(object sender, EventArgs e)
		{
			CurveLibrarySelector selector = new CurveLibrarySelector();
			if (selector.ShowDialog() == DialogResult.OK && selector.SelectedItem != null) {
				// make a new curve that references the selected library curve, and set it to the current Curve
				Curve newCurve = new Curve(selector.SelectedItem.Item2);
				newCurve.LibraryReferenceName = selector.SelectedItem.Item1;
				newCurve.IsCurrentLibraryCurve = false;
				Curve = newCurve;
			}
		}

		private void buttonSaveCurveToLibrary_Click(object sender, EventArgs e)
		{
			Common.Controls.TextDialog dialog = new Common.Controls.TextDialog("Curve name?");

			while (dialog.ShowDialog() == DialogResult.OK) {
				if (dialog.Response == string.Empty) {
					//messageBox Arguments are (Text, Title, No Button Visible, Cancel Button Visible)
					MessageBoxForm.msgIcon = SystemIcons.Error; //this is used if you want to add a system icon to the message form.
					var messageBox = new MessageBoxForm("Please enter a name.", "Curve Name", false, false);
					messageBox.ShowDialog();
					continue;
				}

				if (Library.Contains(dialog.Response)) {
					//messageBox Arguments are (Text, Title, No Button Visible, Cancel Button Visible)
					MessageBoxForm.msgIcon = SystemIcons.Question; //this is used if you want to add a system icon to the message form.
					var messageBox = new MessageBoxForm("There is already a curve with that name. Do you want to overwrite it?", "Overwrite curve?", true, true);
					messageBox.ShowDialog();
					if (messageBox.DialogResult == DialogResult.OK) {
						Library.AddCurve(dialog.Response, new Curve(Curve));
						break;
					}
					if (messageBox.DialogResult == DialogResult.Cancel) {
						break;
					}
				}
				else {
					Library.AddCurve(dialog.Response, new Curve(Curve));
					break;
				}
			}
		}

		private void buttonUnlinkCurve_Click(object sender, EventArgs e)
		{
			Curve.UnlinkFromLibraryCurve();
			PopulateFormWithCurve(Curve);
		}

		private void buttonEditLibraryCurve_Click(object sender, EventArgs e)
		{
			string libraryName = Curve.LibraryReferenceName;

			Library.EditLibraryCurve(libraryName);

			PopulateFormWithCurve(Curve);
		}

		private void btnReverse_Click(object sender, EventArgs e)
		{

			foreach (var curveItem in zedGraphControl.GraphPane.CurveList)
			{
				// Step 1: Reverse X coordinates
				for (int i = 0; i < curveItem.Points.Count; i++)
				{
					curveItem.Points[i].X = 100 - curveItem.Points[i].X;
				}

				var points = curveItem.Points as PointPairList;
				if (points != null)
				{
					// Force re-sort since we modified X values directly
					// (Sort() skips if it thinks the list is already sorted)
					points.SetSorted(false);
					points.Sort();
				}

				// Step 2: After sort, swap in/out handles completely
				// (negate X for horizontal flip, swap Y between in/out)
				for (int i = 0; i < curveItem.Points.Count; i++)
				{
					var h = curveItem.Points[i].Tag as BezierHandleData;
					if (h == null) continue;

					// Save complete old in-handle
					double oldInX = h.InHandleX, oldInY = h.InHandleY;
					bool oldHasIn = h.HasInHandle;

					// New in = negated old out (full swap)
					h.InHandleX = -h.OutHandleX;
					h.InHandleY = h.OutHandleY;
					h.HasInHandle = h.HasOutHandle;

					// New out = negated old in (full swap)
					h.OutHandleX = -oldInX;
					h.OutHandleY = oldInY;
					h.HasOutHandle = oldHasIn;
				}

			}

			UpdateHandleGraphObjects();
			zedGraphControl.GraphPane.AxisChange();
			zedGraphControl.Refresh();
		}

		private void btnInvert_Click(object sender, EventArgs e)
		{
			foreach (var curveItem in zedGraphControl.GraphPane.CurveList)
			{
				for (int i = 0; i < curveItem.Points.Count; i++)
				{
					curveItem.Points[i].Y = 100 - curveItem.Points[i].Y;

					// Invert Bezier handles: negate Y offsets
					var handleData = curveItem.Points[i].Tag as BezierHandleData;
					if (handleData != null)
					{
						handleData.InHandleY = -handleData.InHandleY;
						handleData.OutHandleY = -handleData.OutHandleY;
					}
				}

			}
			UpdateHandleGraphObjects();
			zedGraphControl.Invalidate();
		}

		private void btnUpdateCoordinates_Click(object sender, EventArgs e)
		{
			if(double.TryParse(txtXValue.Text, out var x))
			{
				zedGraphControl.DragEditingPair.X = x;
			}

			if (double.TryParse(txtYValue.Text, out var y))
			{
				zedGraphControl.DragEditingPair.Y = y;
			}
			zedGraphControl.Invalidate();
		}

		private void groupBoxes_Paint(object sender, PaintEventArgs e)
		{
			ThemeGroupBoxRenderer.GroupBoxesDrawBorder(sender, e, Font);
		}

		private void btnDraw_Click(object sender, EventArgs e)
		{
			toolTip.ToolTipTitle = "Draw Curve";
			toolTip.Show("Draw curve from left side to right side of grid using the left mouse button", btnDraw, -100, -400, 4000);
			zedGraphControl.GraphPane.CurveList[0].Clear();
			zedGraphControl.Invalidate();
			_tempX = 0;
			_drawCurve = true;
		}

		private void btnFunctionCurve_Click(object sender, EventArgs e)
		{
			FunctionGenerator fGen = new FunctionGenerator(_holdFunction);
			var result = fGen.ShowDialog(this);
			if (result == DialogResult.Cancel)
			{
				return;
			}
			zedGraphControl.GraphPane.CurveList[0].Clear();
			zedGraphControl.Invalidate();
			//string f = "Sin(2 * Pi * (x / 100)) * ((25-7)/2) + ((25-7) /2) + 7";
			if (string.IsNullOrEmpty(fGen.Function))
			{
				MessageBoxForm mbf = new MessageBoxForm($"The function entered is empty.", "Function Error", MessageBoxButtons.OK, SystemIcons.Error);
				mbf.ShowDialog(this);
				return;
			}

			_holdFunction = fGen.Function;
			try
			{
				var exp = new Expression(fGen.Function, EvaluateOptions.IgnoreCase);
				if (exp.HasErrors())
				{
					MessageBoxForm mbf = new MessageBoxForm($"The function entered has the following syntax errors.\n {exp.Error}","Function Error", MessageBoxButtons.OK, SystemIcons.Error);
					mbf.ShowDialog(this);
					return;
				}

				var step = Convert.ToInt16(textBoxThreshold.Text);
				int lastXValue = 0;
				for (int x = 0; x <= 100; x+=step)
				{
					CalculateFunction(exp, x);
					lastXValue = x;
				}

				if (lastXValue != 100)
				{
					CalculateFunction(exp, 100);
				}
				zedGraphControl.Invalidate();
			}
			catch (Exception ex)
			{
				MessageBoxForm mbf = new MessageBoxForm($"The function entered has the following syntax errors.\n {ex.Message}", "Function Errors", MessageBoxButtons.OK, SystemIcons.Error);
				mbf.ShowDialog(this);
			}
		}

		private void CalculateFunction(Expression exp, int x)
		{
			exp.Parameters["x"] = x;
			exp.Parameters["Pi"] = Math.PI;
			var ans = exp.Evaluate();
			var y = Convert.ToDouble(ans);
			if (y < 0) y = 0;
			if (y > 100) y = 100;
			zedGraphControl.GraphPane.CurveList[0].AddPoint(x, y);
		}

		private void textBoxThreshold_KeyPress(object sender, KeyPressEventArgs e)
		{
			e.Handled = !char.IsDigit(e.KeyChar) && !char.IsControl(e.KeyChar);
		}

		private void textBoxThreshold_TextChanged(object sender, EventArgs e)
		{
			if (textBoxThreshold.Text == "") return;
			if (Convert.ToInt16(textBoxThreshold.Text) > 20)
			{
				textBoxThreshold.Text = @"20";
				toolTip.Show("Draw Threshold has a maximun value of 20", textBoxThreshold, 0, 30, 3000);
			}
		}

		#region Waveform and Marks Rendering

		/// <summary>
		/// Adds waveform and marks to the ZedGraph GraphObjList so they render
		/// as part of ZedGraph's native drawing pipeline.
		/// Call after PopulateFormWithCurve or when waveform data changes.
		/// </summary>
		private void AddWaveformAndMarksToGraph()
		{
			var pane = zedGraphControl.GraphPane;

			// Remove old waveform/marks objects
			for (int i = pane.GraphObjList.Count - 1; i >= 0; i--)
			{
				if (pane.GraphObjList[i].Tag is string tag &&
				    (tag == "waveform_overlay" || tag == "mark_line"))
					pane.GraphObjList.RemoveAt(i);
			}

			// Add waveform as a bitmap ImageObj
			if (WaveformPeaks != null && WaveformPeaks.Length > 0)
			{
				var waveformImage = RenderWaveformBitmap(WaveformPeaks, 800, 200);
				if (waveformImage != null)
				{
					// Position at (0, 100) spanning full chart (width=100, height=100 in curve space)
					var imgObj = new ImageObj(waveformImage, 0, 100, 100, 100);
					imgObj.Location.CoordinateFrame = CoordType.AxisXYScale;
					imgObj.IsScaled = true;
					imgObj.ZOrder = ZOrder.F_BehindGrid;
					imgObj.Tag = "waveform_overlay";
					pane.GraphObjList.Add(imgObj);
				}
			}

			// Add marks as vertical lines
			if (MarkPositions != null && MarkPositions.Count > 0)
			{
				foreach (var (position, color) in MarkPositions)
				{
					var line = new LineObj(Color.FromArgb(80, color), position, 100, position, 0);
					line.Line.Width = 1f;
					line.Line.Style = System.Drawing.Drawing2D.DashStyle.Dash;
					line.Location.CoordinateFrame = CoordType.AxisXYScale;
					line.IsClippedToChartRect = true;
					line.ZOrder = ZOrder.E_BehindCurves;
					line.Tag = "mark_line";
					pane.GraphObjList.Add(line);
				}
			}

			zedGraphControl.Invalidate();
		}

		/// <summary>
		/// Renders the waveform peaks into a bitmap with transparent background.
		/// </summary>
		private Bitmap RenderWaveformBitmap(float[] peaks, int width, int height)
		{
			var bmp = new Bitmap(width, height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);

			using (var g = Graphics.FromImage(bmp))
			{
				g.Clear(Color.Transparent);

				// Draw half-waveform from the bottom up (like a bar graph)
				using (var pen = new Pen(Color.FromArgb(110, 45, 45, 45), 1f))
				{
					for (int px = 0; px < width; px++)
					{
						int sampleIdx = (int)((double)px / width * peaks.Length);
						if (sampleIdx >= peaks.Length) sampleIdx = peaks.Length - 1;

						float peak = peaks[sampleIdx];
						float lineHeight = peak * height;

						g.DrawLine(pen, px, height, px, height - lineHeight);
					}
				}
			}

			return bmp;
		}

		#endregion

		#region Bezier Handle Methods

		/// <summary>
		/// Direct MouseClick handler on ZedGraph control for toggling Bezier handles.
		/// Uses right-click near a point to toggle handles.
		/// </summary>
		private void zedGraphControl_BezierMouseClick(object sender, MouseEventArgs e)
		{
			if (ReadonlyCurve) return;
			if (zedGraphControl.GraphPane.CurveList.Count == 0) return;

			CurveItem curve;
			int pointIndex;

			// Right-click: toggle Bezier handles
			if (e.Button == MouseButtons.Right)
			{
				bool foundPoint = zedGraphControl.GraphPane.FindNearestPoint(e.Location, out curve, out pointIndex);

				if (foundPoint)
				{
					PointPairList pointList = zedGraphControl.GraphPane.CurveList[0].Points as PointPairList;
					if (pointList != null && pointIndex >= 0 && pointIndex < pointList.Count)
					{
						// Debug: directly set Tag and check
						var pt = pointList[pointIndex];
						string beforeTag = pt.Tag == null ? "null" : pt.Tag.GetType().Name;

						ToggleBezierHandles(pointList, pointIndex);
						UpdateHandleGraphObjects();
					}
				}
				else
				{
					MessageBox.Show("Right-click detected but no point found nearby.", "Bezier Debug");
				}
			}
		}

		/// <summary>
		/// Toggles Bezier handles on/off for a curve point.
		/// When enabling, creates default handles with horizontal tangent,
		/// length = 1/3 of distance to neighboring points.
		/// </summary>
		private void ToggleBezierHandles(PointPairList pointList, int pointIndex)
		{
			var point = pointList[pointIndex];

			if (point.Tag is BezierHandleData)
			{
				// Remove handles
				point.Tag = null;
				return;
			}

			// Create new handles with sensible defaults
			var handleData = new BezierHandleData();

			bool isFirst = (pointIndex == 0);
			bool isLast = (pointIndex == pointList.Count - 1);

			handleData.HasInHandle = !isFirst;
			handleData.HasOutHandle = !isLast;

			// Default handle length: 1/3 of the distance to the neighboring point
			if (handleData.HasInHandle)
			{
				double prevX = pointList[pointIndex - 1].X;
				double dist = (point.X - prevX) / 3.0;
				handleData.InHandleX = -dist; // points left
				handleData.InHandleY = 0;     // horizontal tangent
			}

			if (handleData.HasOutHandle)
			{
				double nextX = pointList[pointIndex + 1].X;
				double dist = (nextX - point.X) / 3.0;
				handleData.OutHandleX = dist; // points right
				handleData.OutHandleY = 0;    // horizontal tangent
			}

			point.Tag = handleData;
		}

		/// <summary>
		/// Hit-tests for the nearest Bezier handle grip within a pixel tolerance.
		/// Returns true if a handle grip was found near the mouse position.
		/// </summary>
		private bool FindNearestHandle(Point mouseLocation, out int pointIndex, out bool isInHandle)
		{
			const double PixelTolerance = 8.0;
			pointIndex = -1;
			isInHandle = false;

			if (zedGraphControl.GraphPane.CurveList.Count == 0)
				return false;

			PointPairList pointList = zedGraphControl.GraphPane.CurveList[0].Points as PointPairList;
			if (pointList == null)
				return false;

			double minDistSq = PixelTolerance * PixelTolerance;
			bool found = false;

			for (int i = 0; i < pointList.Count; i++)
			{
				var handleData = pointList[i].Tag as BezierHandleData;
				if (handleData == null) continue;

				double ptX = pointList[i].X;
				double ptY = pointList[i].Y;

				// Check in-handle grip
				if (handleData.HasInHandle)
				{
					PointF gripScreen = TransformToScreen(ptX + handleData.InHandleX, ptY + handleData.InHandleY);
					double dx = gripScreen.X - mouseLocation.X;
					double dy = gripScreen.Y - mouseLocation.Y;
					double distSq = dx * dx + dy * dy;
					if (distSq < minDistSq)
					{
						minDistSq = distSq;
						pointIndex = i;
						isInHandle = true;
						found = true;
					}
				}

				// Check out-handle grip
				if (handleData.HasOutHandle)
				{
					PointF gripScreen = TransformToScreen(ptX + handleData.OutHandleX, ptY + handleData.OutHandleY);
					double dx = gripScreen.X - mouseLocation.X;
					double dy = gripScreen.Y - mouseLocation.Y;
					double distSq = dx * dx + dy * dy;
					if (distSq < minDistSq)
					{
						minDistSq = distSq;
						pointIndex = i;
						isInHandle = false;
						found = true;
					}
				}
			}

			return found;
		}

		/// <summary>
		/// Transforms curve-space coordinates (0-100) to screen pixel coordinates.
		/// </summary>
		private PointF TransformToScreen(double curveX, double curveY)
		{
			var pane = zedGraphControl.GraphPane;
			float screenX = pane.XAxis.Scale.Transform(curveX);
			float screenY = pane.YAxis.Scale.Transform(curveY);
			return new PointF(screenX, screenY);
		}

		/// <summary>
		/// Paints Bezier handle lines and grip dots on top of the ZedGraph control.
		/// </summary>
		/// <summary>
		/// Updates the ZedGraph GraphObjList with handle lines and grip dots.
		/// Uses ZedGraph's native rendering so handles appear correctly.
		/// </summary>
		private void UpdateHandleGraphObjects()
		{
			var pane = zedGraphControl.GraphPane;

			// Remove old handle objects (tagged with "bezier_handle")
			for (int i = pane.GraphObjList.Count - 1; i >= 0; i--)
			{
				if (pane.GraphObjList[i].Tag is string tag && tag == "bezier_handle")
					pane.GraphObjList.RemoveAt(i);
			}

			if (pane.CurveList.Count == 0) return;
			PointPairList pointList = pane.CurveList[0].Points as PointPairList;
			if (pointList == null || !pointList.HasBezierData()) return;

			const double GripSize = 2.0; // in curve-space units

			for (int i = 0; i < pointList.Count; i++)
			{
				var handleData = pointList[i].Tag as BezierHandleData;
				if (handleData == null) continue;

				double ptX = pointList[i].X;
				double ptY = pointList[i].Y;

				// Draw in-handle
				if (handleData.HasInHandle)
				{
					double gripX = Math.Max(0, Math.Min(100, ptX + handleData.InHandleX));
					double gripY = Math.Max(0, Math.Min(100, ptY + handleData.InHandleY));
					AddHandleGraphObjects(pane, ptX, ptY, gripX, gripY, GripSize);
				}

				// Draw out-handle
				if (handleData.HasOutHandle)
				{
					double gripX = Math.Max(0, Math.Min(100, ptX + handleData.OutHandleX));
					double gripY = Math.Max(0, Math.Min(100, ptY + handleData.OutHandleY));
					AddHandleGraphObjects(pane, ptX, ptY, gripX, gripY, GripSize);
				}
			}

			zedGraphControl.Invalidate();
		}

		/// <summary>
		/// Adds a handle line and grip dot to the GraphObjList with clean white styling.
		/// </summary>
		private void AddHandleGraphObjects(GraphPane pane, double ptX, double ptY, double gripX, double gripY, double gripSize)
		{
			// Handle line from anchor to grip
			var line = new LineObj(Color.FromArgb(200, 255, 255, 255), ptX, ptY, gripX, gripY);
			line.Line.Width = 1f;
			line.Line.Style = System.Drawing.Drawing2D.DashStyle.Solid;
			line.IsClippedToChartRect = false;
			line.Location.CoordinateFrame = CoordType.AxisXYScale;
			line.Tag = "bezier_handle";
			pane.GraphObjList.Add(line);

			// Grip dot
			var grip = new EllipseObj(gripX - gripSize / 2, gripY + gripSize / 2, gripSize, gripSize);
			grip.Fill = new Fill(Color.White);
			grip.Border.Color = Color.FromArgb(180, 180, 180);
			grip.Border.Width = 1f;
			grip.Location.CoordinateFrame = CoordType.AxisXYScale;
			grip.IsClippedToChartRect = false;
			grip.Tag = "bezier_handle";
			pane.GraphObjList.Add(grip);
		}

		#endregion
	}
}