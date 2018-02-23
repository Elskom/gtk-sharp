﻿// GtkSharp.Generation.StructBase.cs - The Structure/Boxed Base Class.
//
// Author: Mike Kestner <mkestner@speakeasy.net>
//
// Copyright (c) 2001-2003 Mike Kestner
//
// This program is free software; you can redistribute it and/or
// modify it under the terms of version 2 of the GNU General Public
// License as published by the Free Software Foundation.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the GNU
// General Public License for more details.
//
// You should have received a copy of the GNU General Public
// License along with this program; if not, write to the
// Free Software Foundation, Inc., 59 Temple Place - Suite 330,
// Boston, MA 02111-1307, USA.

using System;
using System.IO;
using System.Xml;
using System.Collections.Generic;

namespace GtkSharp.Generation {

	public abstract class StructBase : ClassBase, IManualMarshaler {

		internal new List<StructField> fields = new List<StructField>();

		protected StructBase (XmlElement ns, XmlElement elem) : base (ns, elem)
		{
			foreach (XmlNode node in elem.ChildNodes) {

				if (!(node is XmlElement)) continue;
				XmlElement member = (XmlElement) node;

				switch (node.Name) {
				case Constants.Field:
					fields.Add (new StructField (member, this));
					break;

				case Constants.Callback:
					Statistics.IgnoreCount++;
					break;

				default:
					if (!IsNodeNameHandled (node.Name))
						Console.WriteLine ("Unexpected node " + node.Name + " in " + CName);
					break;
				}
			}
		}

		public override string DefaultValue {
			get {
				return QualifiedName + ".Zero";
			}
		}

		public override string MarshalReturnType {
			get {
				return "IntPtr";
			}
		}

		public override string MarshalCallbackType {
			get {
				return "IntPtr";
			}
		}

		public override string MarshalType {
			get {
				return "ref " + QualifiedName;
			}
		}

		public override string AssignToName {
			get { throw new NotImplementedException (); }
		}

		public override string CallByName ()
		{
			return "ref this";
		}

		public override string CallByName (string var)
		{
			return "ref " + var;
		}

		public override string FromNative (string var)
		{
			if (DisableNew)
				return var + " == IntPtr.Zero ? " + QualifiedName + ".Zero : System.Runtime.InteropServices.Marshal.PtrToStructure<" + QualifiedName + "> (var)";
			else
				return QualifiedName + ".New (" + var + ")";
		}
		
		public string AllocNative (string var)
		{
			return "GLib.Marshaller.StructureToPtrAlloc<" + QualifiedName + "> (" + var + ")";
		}

		public string ReleaseNative (string var)
		{
			return "Marshal.FreeHGlobal (" +var + ")";
		}

		private bool DisableNew {
			get {
				return Elem.HasAttribute (Constants.DisableNew);
			}
		}

		protected new void GenFields (GenerationInfo gen_info)
		{
			int bitfields = 0;
			bool need_field = true;

			foreach (StructField field in fields) {
				if (field.IsBitfield) {
					if (need_field) {
						StreamWriter sw = gen_info.Writer;

						sw.WriteLine ("\t\tprivate uint _bitfield{0};\n", bitfields++);
						need_field = false;
					}
				} else
					need_field = true;
				field.Generate (gen_info, "\t\t");
			}
		}

		public override bool Validate ()
		{
			foreach (StructField field in fields) {
				if (!field.Validate ()) {
					Console.WriteLine ("in Struct " + QualifiedName);
					if (!field.IsPointer)
						return false;
				}
			}

			return base.Validate ();
		}

		string GetInterfaceImplExtra ()
		{
			if (Elem.HasAttribute (Constants.IEquatable) && Elem.GetAttribute (Constants.IEquatable) == "1")
				return " : IEquatable<" + Name + ">";
			return string.Empty;
		}

		public override void Generate (GenerationInfo gen_info)
		{
			bool need_close = false;
			if (gen_info.Writer == null) {
				gen_info.Writer = gen_info.OpenStream (Name);
				need_close = true;
			}

			StreamWriter sw = gen_info.Writer;
			
			sw.WriteLine ("namespace " + NS + " {");
			sw.WriteLine ();
			sw.WriteLine ("\tusing System;");
			sw.WriteLine ("\tusing System.Collections;");
			sw.WriteLine ("\tusing System.Runtime.InteropServices;");
			sw.WriteLine ();
			
			sw.WriteLine ("#region Autogenerated code");
			if (IsDeprecated)
				sw.WriteLine ("\t[Obsolete]");
			sw.WriteLine ("\t[StructLayout(LayoutKind.Sequential)]");
			GenerateAttribute (sw);
			string access = IsInternal ? "internal" : "public";
			sw.WriteLine ("\t" + access + " struct " + Name + GetInterfaceImplExtra () + " {");
			sw.WriteLine ();

			GenFields (gen_info);
			sw.WriteLine ();
			GenCtors (gen_info);
			GenMethods (gen_info, null, this);

			if (!need_close)
				return;

			sw.WriteLine ("#endregion");
			AppendCustom(sw, gen_info.CustomDir);
			
			sw.WriteLine ("\t}");
			sw.WriteLine ("}");

			sw.Close ();
			gen_info.Writer = null;
		}

		protected virtual void GenerateAttribute (StreamWriter writer)
		{
			if (GetMethod ("GetType") != null || GetMethod ("GetGType") != null)
				writer.WriteLine ("\t[{0}]", Name);
		}

		void GenNewWithMarshal (StreamWriter sw)
		{
			sw.WriteLine ("\t\tpublic static " + QualifiedName + " New (IntPtr raw) {");
			sw.WriteLine ("\t\t\tif (raw == IntPtr.Zero)");
			sw.WriteLine ("\t\t\t\treturn {0}.Zero;", QualifiedName);
			sw.WriteLine ("\t\t\treturn Marshal.PtrToStructure<{0}> (raw);", QualifiedName);
			sw.WriteLine ("\t\t}");
			sw.WriteLine ();
		}

		void GenNewWithMemCpy (StreamWriter sw)
		{
			sw.WriteLine ("\t\tpublic static " + QualifiedName + " New (IntPtr raw) {");
			sw.WriteLine ("\t\t\tif (raw == IntPtr.Zero)");
			sw.WriteLine ("\t\t\t\treturn {0}.Zero;", QualifiedName);
			sw.WriteLine ("\t\t\tunsafe {{ return *({0}*)raw; }}", QualifiedName);
			sw.WriteLine ("\t\t}");
			sw.WriteLine ();
		}

		protected override void GenCtors (GenerationInfo gen_info)
		{
			StreamWriter sw = gen_info.Writer;

			sw.WriteLine ("\t\tpublic static {0} Zero = new {0} ();", QualifiedName);
			sw.WriteLine();
			if (!DisableNew) {
				bool viaMarshal = !SymbolTable.Table.IsBlittable(SymbolTable.Table[this.CName]);

				if (viaMarshal)
					GenNewWithMarshal (sw);
				else
					GenNewWithMemCpy (sw);
			}

			foreach (Ctor ctor in Ctors)
				ctor.IsStatic = true;

			base.GenCtors (gen_info);
		}

		public override void Prepare (StreamWriter sw, string indent)
		{
		}

		public override void Finish (StreamWriter sw, string indent)
		{
		}
	}
}

