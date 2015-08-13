﻿using System;
using System.Collections.Generic;

using Android;
using Android.Views;
using Android.Widget;

using Rollify.Core;

namespace RollifyAndroid
{
	public class FormulaAdapter : BaseAdapter<Formula>
	{
		public Formula[] formulas;
		MainActivity context;

		public FormulaAdapter (MainActivity context, Formula[] formulas)
		{
			this.context = context;
			this.formulas = formulas;
		}

		public override long GetItemId(int position) {
			return formulas[position].ID; //TODO check to see what itemId is for
		}

		public override Formula this[int position] {
			get { return formulas[position]; }
		}

		public override int Count {
			get { return formulas.Length; }
		}

		public override View GetView(int position, View convertView, ViewGroup parent) {
			View view = convertView ?? context.LayoutInflater.Inflate (Android.Resource.Layout.SimpleListItem1, null);
			view.FindViewById<TextView> (Android.Resource.Id.Text1).Text = formulas [position].Expression;
			return view;
		}
	}
}
