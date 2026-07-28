namespace comviaServer.Model
{
    public class Recommendation
    {
        public string RecommendationType { get; set; }   

        public string OriginalSKU { get; set; }           
        public string SuggestedSKU { get; set; }          

        public string OriginalSupplyConfig { get; set; }  
        public string SuggestedSupplyConfig { get; set; } 

        public int OriginalQuantity { get; set; }      
        public int? SuggestedQuantity { get; set; }       

        public decimal OriginalUnitPrice { get; set; }    
        public decimal SuggestedUnitPrice { get; set; }
        public decimal PriceDifference { get; set; }     
        public decimal TotalSavingAmount { get; set; }   
        public decimal TotalNewCost { get; set; }

        public decimal SavingPercent { get; set; }       

        public string Explanation { get; set; }
    }
}
