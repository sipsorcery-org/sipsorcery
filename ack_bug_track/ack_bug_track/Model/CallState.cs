using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ack_bug_track.Model
{
	public enum CallState
	{
		Idle,
		RxInvited,
		TxInvited,
		Connected,
		RxOnHold,
		TxOnHold,
		RxOffHold,
		TxOffHold
	}
}
