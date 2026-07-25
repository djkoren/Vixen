using Vixen.Module.Effect;
using Vixen.Sys;

namespace VixenModules.Effect.Lightning
{
	public class LightningDescriptor : EffectModuleDescriptorBase
	{
		// New unique GUID for Lightning effect
		private static Guid _typeId = new Guid("{A5E7C5F2-B4D8-4E3A-9F2C-7A1D8B6F9E14}");
		private static Guid _ColorGradientId = new Guid("{64f4ab26-3ed4-49a3-a004-23656ed0424a}");

		public override string EffectName
		{
			get { return "Lightning"; }
		}

		public override EffectGroups EffectGroup
		{
			get { return EffectGroups.Basic; }
		}

		public override bool SupportsMarks => true;

		public override Guid TypeId
		{
			get { return _typeId; }
		}

		public override Type ModuleClass
		{
			get { return typeof(Lightning); }
		}

		public override Type ModuleDataClass
		{
			get { return typeof(LightningData); }
		}

		public override string Author
		{
			get { return "Bezier Build"; }
		}

		public override string TypeName
		{
			get { return EffectName; }
		}

		public override string Description
		{
			get { return "Random lightning flashes across the elements, with optional bolt-style sequential flashes."; }
		}

		public override string Version
		{
			get { return "1.0"; }
		}

		public override Guid[] Dependencies
		{
			get { return new Guid[] { _ColorGradientId }; }
		}

		public override ParameterSignature Parameters
		{
			get { return new ParameterSignature(); }
		}
	}
}
