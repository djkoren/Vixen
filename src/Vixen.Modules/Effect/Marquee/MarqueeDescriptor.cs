using Vixen.Module.Effect;
using Vixen.Sys;

namespace VixenModules.Effect.Marquee
{
	public class MarqueeDescriptor : EffectModuleDescriptorBase
	{
		private static readonly Guid _typeId = new Guid("9DE5A327-AF69-4472-B8C9-704B03A6AA43");

		public override ParameterSignature Parameters
		{
			get { return new ParameterSignature(); }
		}

		public override EffectGroups EffectGroup
		{
			get { return EffectGroups.Pixel; }
		}

		public override string TypeName
		{
			get { return EffectName; }
		}

		public override Guid TypeId
		{
			get { return _typeId; }
		}

		public override Type ModuleClass
		{
			get { return typeof(Marquee); }
		}

		public override Type ModuleDataClass
		{
			get { return typeof(MarqueeData); }
		}

		public override string Author
		{
			get { return "Vixen Team"; }
		}

		public override string Description
		{
			get { return "Old-fashioned marquee chase with smooth slow motion, per-LED fading and randomization"; }
		}

		public override string Version
		{
			get { return "1.0"; }
		}

		public override string EffectName
		{
			get { return "Marquee"; }
		}
	}
}
