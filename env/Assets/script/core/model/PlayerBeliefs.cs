using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace script.core.model
{
    [System.Serializable]
    public class PlayerBeliefs : AgentBeliefs
    {
        public float budget;
        public float tokens;
        public float tokenPrice;

        public PlayerBeliefs(float budget, float tokenPrice)
        {
            Tokens = 0.0f;
            Budget = budget;
            TokenPrice = tokenPrice;
        }

        public PlayerBeliefs()
        {
            Budget = 0.0f;
            Tokens = 0.0f;
            TokenPrice = 2.0f;
        }

        public float Budget
        {
            get
            {
                return budget;
            }
            set
            {
                budget = value;
            }
        }

        public float Tokens
        {
            get
            {
                return tokens;
            }
            set
            {
                tokens = value;
            }
        }

        public float TokenPrice
        {
            get
            {
                return tokenPrice;
            }
            set
            {
                tokenPrice = value;
            }
        }

        public override string GetBeliefsAsLiterals()
        {
            StringBuilder beliefs = new StringBuilder();
            beliefs.Append($"budget({Budget})");
            beliefs.Append($", tokens({Tokens})");
            beliefs.Append($", token_price({TokenPrice})");

            if (friends != null && friends.Count != 0)
            {
                string temp = "[" + string.Join(", ", friends.Select(item => item.ToString())) + "]";
                beliefs.Append($", friends({temp})");
            }
            else
            {
                beliefs.Append($", friends([])");
            }        

            return beliefs.ToString();
        }
    }
}