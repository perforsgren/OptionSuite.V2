using System;
using System.Globalization;
using System.IO;
using System.Xml;
using FxTradeHub.Contracts.Dtos;
using FxSharedConfig;

namespace FxTradeHub.Services.Mx3Export
{
    /// <summary>
    /// Service för att exportera NDF trades till MX3 XML-format.
    /// Non-Deliverable Forward har specialstruktur med fxFixing och settlementCurrency.
    /// </summary>
    public sealed class Mx3NdfExportService
    {
        public Mx3OptionExportResult CreateXmlFile(Mx3NdfExportRequest request)
        {
            try
            {
                var exportFolder = AppPaths.Mx3ImportFolder;
                var fileName = $"{request.StpTradeId}_{request.TradeId}.xml";
                var fullPath = Path.Combine(exportFolder, fileName);

                var xmlDoc = BuildXmlDocument(request);
                xmlDoc.Save(fullPath);

                return new Mx3OptionExportResult
                {
                    Success = true,
                    FileName = fileName,
                    FilePath = fullPath
                };
            }
            catch (Exception ex)
            {
                return new Mx3OptionExportResult
                {
                    Success = false,
                    ErrorMessage = ex.Message
                };
            }
        }

        private XmlDocument BuildXmlDocument(Mx3NdfExportRequest req)
        {
            var xmlDoc = new XmlDocument();

            // Validering
            if (string.IsNullOrEmpty(req.CurrencyPair) || req.CurrencyPair.Length != 6)
                throw new ArgumentException($"Invalid CurrencyPair: {req.CurrencyPair}");

            if (string.IsNullOrEmpty(req.SettlementCurrency) || req.SettlementCurrency.Length != 3)
                throw new ArgumentException($"Invalid SettlementCurrency: {req.SettlementCurrency}");

            // Parse currency pair
            var baseCcy = req.CurrencyPair.Substring(0, 3);   // USD
            var quoteCcy = req.CurrencyPair.Substring(3, 3);  // CNY

            // ✅ FIX: Notional är alltid i NotionalCurrency (som är i request objektet)
            // Vi behöver avgöra vilket som är vilket baserat på NotionalCurrency från BlotterTradeRow

            decimal notional1;
            decimal notional2;
            string ccy1;
            string ccy2;

            // Avgör vilken valuta Notional är i och beräkna den andra
            // Rate är alltid i termer av CurrencyPair (base/quote)
            if (req.BuySell == "Buy")
            {
                // Buy = vi köper base currency (USD i USDCNY)
                // Om Notional är i quote currency (CNY):
                //   - Vi betalar CNY (notional)
                //   - Vi får USD (notional / rate)
                // Om Notional är i base currency (USD):
                //   - Vi betalar quote (notional * rate)
                //   - Vi får base (notional)

                // Eftersom din data visar Notional=1000000 CNY, NotionalCurrency=CNY
                // och det är en BUY av USD:
                ccy1 = quoteCcy;  // CNY (vi betalar)
                ccy2 = baseCcy;   // USD (vi får)
                notional1 = req.Notional;  // 1,000,000 CNY
                notional2 = req.Notional / req.Rate;  // 1,000,000 / 6.9823 = 143,214.77 USD
            }
            else
            {
                // Sell = vi säljer base currency (USD i USDCNY)
                ccy1 = baseCcy;   // USD (vi betalar)
                ccy2 = quoteCcy;  // CNY (vi får)
                notional1 = req.Notional / req.Rate;  // USD amount
                notional2 = req.Notional;  // CNY amount
            }

            // Format dates
            var tradedate = req.TradeDate.ToString("yyyyMMdd");
            var settlementdate = req.SettlementDate.ToString("yyyyMMdd");
            var fixingdate = req.FixingDate.ToString("yyyyMMdd");

            #region Root: MxML
            var rootNode = xmlDoc.CreateElement("MxML");
            var versionAttr = xmlDoc.CreateAttribute("version");
            versionAttr.Value = "1-1";
            rootNode.Attributes.Append(versionAttr);
            xmlDoc.AppendChild(rootNode);
            #endregion

            #region events
            var eventsNode = xmlDoc.CreateElement("events");
            rootNode.AppendChild(eventsNode);

            var mainEventNode = xmlDoc.CreateElement("mainEvent");
            eventsNode.AppendChild(mainEventNode);

            AppendElement(xmlDoc, mainEventNode, "action", "insertion");

            var objectNode = xmlDoc.CreateElement("object");
            SetAttribute(xmlDoc, objectNode, "href", "#contract_0");
            mainEventNode.AppendChild(objectNode);
            AppendElement(xmlDoc, objectNode, "objectNature", "contract");

            var inputByNode = xmlDoc.CreateElement("inputBy");
            mainEventNode.AppendChild(inputByNode);
            AppendElement(xmlDoc, inputByNode, "userName", req.Trader);
            AppendElement(xmlDoc, inputByNode, "group", "FO");
            AppendElement(xmlDoc, inputByNode, "desk", "STKFXD");
            #endregion

            #region contracts
            var contractsNode = xmlDoc.CreateElement("contracts");
            rootNode.AppendChild(contractsNode);

            var contractNode = xmlDoc.CreateElement("contract");
            SetAttribute(xmlDoc, contractNode, "id", "contract_0");
            SetAttribute(xmlDoc, contractNode, "mefClass", "mxContractISINGLE");
            SetAttribute(xmlDoc, contractNode, "mefClassInstanceLabel", "Spot-Forward");
            contractsNode.AppendChild(contractNode);

            var contractHeaderNode = xmlDoc.CreateElement("contractHeader");
            contractNode.AppendChild(contractHeaderNode);

            var contractCategoryNode = xmlDoc.CreateElement("contractCategory");
            contractHeaderNode.AppendChild(contractCategoryNode);
            AppendElement(xmlDoc, contractCategoryNode, "typology", "FXD: Non Deliv Fwd");

            var contractSourceNode = xmlDoc.CreateElement("contractSource");
            contractHeaderNode.AppendChild(contractSourceNode);
            AppendElement(xmlDoc, contractSourceNode, "sourceModule", "mx");

            var contractComponentsNode = xmlDoc.CreateElement("contractComponents");
            contractNode.AppendChild(contractComponentsNode);

            var contractComponentNode = xmlDoc.CreateElement("contractComponent");
            contractComponentsNode.AppendChild(contractComponentNode);
            AppendElement(xmlDoc, contractComponentNode, "typologyId", "0");

            var tradeReferenceNode = xmlDoc.CreateElement("tradeReference");
            SetAttribute(xmlDoc, tradeReferenceNode, "href", "#trade_0");
            contractComponentNode.AppendChild(tradeReferenceNode);
            #endregion

            #region trades
            var tradesNode = xmlDoc.CreateElement("trades");
            rootNode.AppendChild(tradesNode);

            var tradeNode = xmlDoc.CreateElement("trade");
            SetAttribute(xmlDoc, tradeNode, "id", "trade_0");
            SetAttribute(xmlDoc, tradeNode, "mefClass", "mxContractITRADE");
            tradesNode.AppendChild(tradeNode);

            BuildTradeParties(xmlDoc, tradeNode);
            BuildTradePortfolios(xmlDoc, tradeNode, req.Portfolio);
            BuildTradeHeader(xmlDoc, tradeNode, tradedate, req.Portfolio, req.Trader, req.TradeId);
            BuildTradeBody(xmlDoc, tradeNode, req.Portfolio, ccy1, ccy2, notional1, notional2,
                settlementdate, fixingdate, req.Rate, req.CurrencyPair, req.SettlementCurrency, req.FixingSource);
            BuildTradeInputConditions(xmlDoc, tradeNode, req.Trader, tradedate);
            #endregion

            return xmlDoc;
        }

        #region Helper methods

        private void BuildTradeParties(XmlDocument xmlDoc, XmlNode tradeNode)
        {
            var partiesNode = xmlDoc.CreateElement("parties");
            tradeNode.AppendChild(partiesNode);

            var party1 = xmlDoc.CreateElement("party");
            SetAttribute(xmlDoc, party1, "id", "SWEDBANK");
            partiesNode.AppendChild(party1);
            AppendElement(xmlDoc, party1, "partyName", "SWEDBANK");

            var party2 = xmlDoc.CreateElement("party");
            SetAttribute(xmlDoc, party2, "id", "_24");
            partiesNode.AppendChild(party2);
            AppendElement(xmlDoc, party2, "partyName", "HEDGE");
        }

        private void BuildTradePortfolios(XmlDocument xmlDoc, XmlNode tradeNode, string portfolio)
        {
            var portfoliosNode = xmlDoc.CreateElement("portfolios");
            tradeNode.AppendChild(portfoliosNode);

            var portfolioNode = xmlDoc.CreateElement("portfolio");
            SetAttribute(xmlDoc, portfolioNode, "id", portfolio);
            portfoliosNode.AppendChild(portfolioNode);
            AppendElement(xmlDoc, portfolioNode, "portfolioLabel", portfolio);
        }

        private void BuildTradeHeader(XmlDocument xmlDoc, XmlNode tradeNode, string tradedate,
            string portfolio, string trader, string tradeId)
        {
            var tradeHeaderNode = xmlDoc.CreateElement("tradeHeader");
            tradeNode.AppendChild(tradeHeaderNode);

            AppendElement(xmlDoc, tradeHeaderNode, "tradeDate", tradedate);

            // ✅ NDF-specifik tradeCategory
            var tradeCategoryNode = xmlDoc.CreateElement("tradeCategory");
            tradeHeaderNode.AppendChild(tradeCategoryNode);
            AppendElement(xmlDoc, tradeCategoryNode, "tradeDestination", "external");
            AppendElement(xmlDoc, tradeCategoryNode, "tradeFamily", "CURR");
            AppendElement(xmlDoc, tradeCategoryNode, "tradeGroup", "FXD");
            AppendElement(xmlDoc, tradeCategoryNode, "tradeType", "FXD");
            AppendElement(xmlDoc, tradeCategoryNode, "typology", "FXD: Non Deliv Fwd");
            AppendElement(xmlDoc, tradeCategoryNode, "mainTypologyPath", "/Typologies/ALL/FXD: Non Deliv Fwd");

            var tradeViewsNode = xmlDoc.CreateElement("tradeViews");
            tradeHeaderNode.AppendChild(tradeViewsNode);

            // View 1: Swedbank
            var tradeView1 = xmlDoc.CreateElement("tradeView");
            tradeViewsNode.AppendChild(tradeView1);

            var partyRef1 = xmlDoc.CreateElement("partyReference");
            SetAttribute(xmlDoc, partyRef1, "href", "#SWEDBANK");
            tradeView1.AppendChild(partyRef1);

            var portfolioRef = xmlDoc.CreateElement("portfolioReference");
            SetAttribute(xmlDoc, portfolioRef, "href", "#" + portfolio);
            partyRef1.AppendChild(portfolioRef);

            AppendElement(xmlDoc, tradeView1, "userName", trader);

            // View 2: Counterparty
            var tradeView2 = xmlDoc.CreateElement("tradeView");
            tradeViewsNode.AppendChild(tradeView2);

            var partyRef2 = xmlDoc.CreateElement("partyReference");
            SetAttribute(xmlDoc, partyRef2, "href", "#_24");
            tradeView2.AppendChild(partyRef2);

            // User defined fields
            var udfNode = xmlDoc.CreateElement("tradeUserDefinedFields");
            tradeHeaderNode.AppendChild(udfNode);

            var foCommentNode = xmlDoc.CreateElement("userDefinedField");
            udfNode.AppendChild(foCommentNode);
            AppendElement(xmlDoc, foCommentNode, "fieldLabel", "FO_COMMENT");
            AppendElement(xmlDoc, foCommentNode, "fieldValue", tradeId);
            AppendElement(xmlDoc, foCommentNode, "fieldType", "character");
        }

        private void BuildTradeBody(XmlDocument xmlDoc, XmlNode tradeNode, string portfolio,
            string ccy1, string ccy2, decimal notional1, decimal notional2,
            string settlementdate, string fixingdate, decimal rate, string ccyPair,
            string settlementCurrency, string fixingSource)
        {
            var tradeBodyNode = xmlDoc.CreateElement("tradeBody");
            tradeNode.AppendChild(tradeBodyNode);

            // ✅ NDF använder fxSpotForward som wrapper, nonDeliverableForward är en UNDERNODE
            var fxSpotForwardNode = xmlDoc.CreateElement("fxSpotForward");
            tradeBodyNode.AppendChild(fxSpotForwardNode);

            // currency1Flow (we PAY)
            var currency1FlowNode = xmlDoc.CreateElement("currency1Flow");
            fxSpotForwardNode.AppendChild(currency1FlowNode);

            var payerPartyRef1 = xmlDoc.CreateElement("payerPartyReference");
            SetAttribute(xmlDoc, payerPartyRef1, "href", "#SWEDBANK");
            currency1FlowNode.AppendChild(payerPartyRef1);

            var payerPortfolioRef = xmlDoc.CreateElement("portfolioReference");
            SetAttribute(xmlDoc, payerPortfolioRef, "href", "#" + portfolio);
            payerPartyRef1.AppendChild(payerPortfolioRef);

            var receiverPartyRef1 = xmlDoc.CreateElement("receiverPartyReference");
            SetAttribute(xmlDoc, receiverPartyRef1, "href", "#_24");
            currency1FlowNode.AppendChild(receiverPartyRef1);

            AppendElement(xmlDoc, currency1FlowNode, "date", settlementdate);
            AppendElement(xmlDoc, currency1FlowNode, "currency", ccy1);
            AppendElement(xmlDoc, currency1FlowNode, "amount", notional1.ToString(CultureInfo.InvariantCulture));

            // currency2Flow (we RECEIVE)
            var currency2FlowNode = xmlDoc.CreateElement("currency2Flow");
            fxSpotForwardNode.AppendChild(currency2FlowNode);

            var payerPartyRef2 = xmlDoc.CreateElement("payerPartyReference");
            SetAttribute(xmlDoc, payerPartyRef2, "href", "#_24");
            currency2FlowNode.AppendChild(payerPartyRef2);

            var receiverPartyRef2 = xmlDoc.CreateElement("receiverPartyReference");
            SetAttribute(xmlDoc, receiverPartyRef2, "href", "#SWEDBANK");
            currency2FlowNode.AppendChild(receiverPartyRef2);

            var receiverPortfolioRef = xmlDoc.CreateElement("portfolioReference");
            SetAttribute(xmlDoc, receiverPortfolioRef, "href", "#" + portfolio);
            receiverPartyRef2.AppendChild(receiverPortfolioRef);

            AppendElement(xmlDoc, currency2FlowNode, "date", settlementdate);
            AppendElement(xmlDoc, currency2FlowNode, "currency", ccy2);
            AppendElement(xmlDoc, currency2FlowNode, "amount", notional2.ToString(CultureInfo.InvariantCulture));

            // exchangeRate
            var exchangeRateNode = xmlDoc.CreateElement("exchangeRate");
            fxSpotForwardNode.AppendChild(exchangeRateNode);

            var fxQuotationNode = xmlDoc.CreateElement("fxQuotation");
            exchangeRateNode.AppendChild(fxQuotationNode);

            AppendElement(xmlDoc, fxQuotationNode, "currency1", ccyPair.Substring(3, 3));
            AppendElement(xmlDoc, fxQuotationNode, "currency2", ccyPair.Substring(0, 3));
            AppendElement(xmlDoc, fxQuotationNode, "fxQuoteBasis", "currency1PerCurrency2");
            AppendElement(xmlDoc, fxQuotationNode, "formFactor", "1");

            AppendElement(xmlDoc, exchangeRateNode, "rate", rate.ToString(CultureInfo.InvariantCulture));

            // ✅ NDF-SEKTION - som UNDERNODE till fxSpotForward
            var ndfNode = xmlDoc.CreateElement("nonDeliverableForward");
            fxSpotForwardNode.AppendChild(ndfNode);

            // settlementCurrency
            AppendElement(xmlDoc, ndfNode, "settlementCurrency", settlementCurrency);

            // fxFixing
            var fxFixingNode = xmlDoc.CreateElement("fxFixing");
            ndfNode.AppendChild(fxFixingNode);

            AppendElement(xmlDoc, fxFixingNode, "currency1", ccyPair.Substring(0, 3));  // ✅ USD
            AppendElement(xmlDoc, fxFixingNode, "currency2", ccyPair.Substring(3, 3));  // ✅ CNY

            var fxRateSourceNode = xmlDoc.CreateElement("fxRateSource");
            fxFixingNode.AppendChild(fxRateSourceNode);
            AppendElement(xmlDoc, fxRateSourceNode, "sourceLabel", fixingSource ?? "NDF_group");
            AppendElement(xmlDoc, fxRateSourceNode, "columnLabel", "Fixing");

            AppendElement(xmlDoc, fxFixingNode, "date", fixingdate);

            // ndfEstimationDate
            AppendElement(xmlDoc, ndfNode, "ndfEstimationDate", "fixingValueDate");

            // ✅ Lägg till obligatoriska fält för fxSpotForward
            AppendElement(xmlDoc, fxSpotForwardNode, "fxAsianAverageRounding", "Disabled");
            AppendElement(xmlDoc, fxSpotForwardNode, "forwardDelivery", "forward");
            AppendElement(xmlDoc, fxSpotForwardNode, "fxQuantitiesInputMode", "oneQuantity");
            AppendElement(xmlDoc, fxSpotForwardNode, "riskSection", ccyPair);
            AppendElement(xmlDoc, fxSpotForwardNode, "offsettingBuySellContribution", "false");
        }


        private void BuildTradeInputConditions(XmlDocument xmlDoc, XmlNode tradeNode, string trader, string tradedate)
        {
            var tradeInputConditionsNode = xmlDoc.CreateElement("tradeInputConditions");
            tradeNode.AppendChild(tradeInputConditionsNode);

            var inputByNode = xmlDoc.CreateElement("inputBy");
            tradeInputConditionsNode.AppendChild(inputByNode);

            AppendElement(xmlDoc, inputByNode, "userName", trader);
            AppendElement(xmlDoc, tradeInputConditionsNode, "systemDate", tradedate);
        }

        private void AppendElement(XmlDocument xmlDoc, XmlNode parentNode, string elementName, string innerText)
        {
            var element = xmlDoc.CreateElement(elementName);
            element.InnerText = innerText;
            parentNode.AppendChild(element);
        }

        private void SetAttribute(XmlDocument xmlDoc, XmlNode node, string attributeName, string value)
        {
            var attribute = xmlDoc.CreateAttribute(attributeName);
            attribute.Value = value;
            node.Attributes.Append(attribute);
        }

        #endregion
    }
}
