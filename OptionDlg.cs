using System;
using System.Windows.Forms;
using PlayingCards;

/*
 * Primary class defines the partial class of the options dialog for the
 * Crescent Solitaire game.
 *
 * Author:  M. G. Slack
 * Written: 2014-01-02
 *
 * ----------------------------------------------------------------------------
 * 
 * Updated: yyyy-mm-dd - xxxx.
 *
 */
namespace CrescentSolitaire
{
    public partial class OptionsWin : Form
    {
        #region Properties
        private CardBacks _cardBack = CardBacks.Spheres;
        public CardBacks CardBack { get { return _cardBack; } set { _cardBack = value; } }

        private bool _shufflePiles = false;
        public bool ShufflePiles { get { return _shufflePiles; } set { _shufflePiles = value; } }

        private bool _showCardBack = false;
        public bool ShowCardBack { get { return _showCardBack; } set { _showCardBack = value; } }

        private PlayingCardImage _images = null;
        public PlayingCardImage Images { set { _images = value; } }

        #endregion

        // --------------------------------------------------------------------

        public OptionsWin()
        {
            InitializeComponent();
        }

        // --------------------------------------------------------------------

        #region Event Handlers

        private void OptionsWin_Load(object sender, EventArgs e)
        {
            int idx = 0;
            
            cbShuffle.Checked = _shufflePiles;
            cbShowBack.Checked = _showCardBack;

            foreach (string name in Enum.GetNames(typeof(CardBacks))) {
                cbImage.Items.Add(name);
            }

            foreach (int val in Enum.GetValues(typeof(CardBacks))) {
                if (val == (int) _cardBack) {
                    idx = (int) _cardBack - (int) CardBacks.Spheres;
                }
            }
            cbImage.SelectedIndex = idx;

            if (_images != null) {
                pbBack.Image = _images.GetCardBackImage(_cardBack);
            }
        }

        private void cbShuffle_CheckedChanged(object sender, EventArgs e)
        {
            _shufflePiles = cbShuffle.Checked;
        }

        private void cbShowBack_CheckedChanged(object sender, EventArgs e)
        {
            _showCardBack = cbShowBack.Checked;
        }

        private void cbImage_SelectedIndexChanged(object sender, EventArgs e)
        {
            _cardBack = (CardBacks) (cbImage.SelectedIndex + (int) CardBacks.Spheres);
            if (_images != null) {
                pbBack.Image = _images.GetCardBackImage(_cardBack);
            }
        }

        #endregion
    }
}
