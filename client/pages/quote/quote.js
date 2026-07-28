$(document).ready(function () {
  const apiRoot = BASE_URL + "/api/quotes";
  const suppliersRoot = BASE_URL + "/api/suppliers";
  const componentsRoot = BASE_URL + "/api/components";
  const quoteID = new URLSearchParams(window.location.search).get("quoteID");

  if (!quoteID) {
    alert("מזהה הצעה חסר");
    return;
  }

  let quoteData = null;

  function formatDate(dateStr) {
    const date = new Date(dateStr);
    return (
      date.getDate() + "-" + (date.getMonth() + 1) + "-" + date.getFullYear()
    );
  }

  ajaxCall("GET", apiRoot + "/" + quoteID, "", getQuoteSCB, getQuoteECB);

  function getQuoteSCB(data) {
    quoteData = data;

    $("#quoteSubtitle").text("הצעה מספר #" + data.quoteID);
    $("#quoteID").text("#" + data.quoteID);
    $("#customerName").text(data.customer.customerName);
    $("#createdDate").text(formatDate(data.createdDate));
    $("#status").html(getStatusLabel(data.status));
    $("#finalTotalPrice").text(
      "$" + parseFloat(data.finalTotalPrice).toFixed(2),
    );
    $("#totalProfit").text("$" + parseFloat(data.totalProfit).toFixed(2));

    if (data.searchResultsJson) {
      $("#editQuoteBtn").show();
    }

    $("#quoteItemsSection").show();

    if (data.items && data.items.length > 0) {
      // Completed quote — enrich items with supplier/component names
      loadAndRenderItems(data.items);
    } else if (data.searchResultsJson) {
      // Draft/Processing — show from search results
      renderFromSearchResults(data.searchResultsJson);
    } else {
      $("#quoteItemsBody").html(
        '<tr><td colspan="8" class="table-empty">אין פריטים להצגה</td></tr>',
      );
    }
  }

  // ── Completed quote: items from DB, enrich with supplier/component ──
  function loadAndRenderItems(items) {
    const uniqueSupplierIds = [
      ...new Set(items.map((i) => i.supplierID).filter(Boolean)),
    ];
    const uniqueSkus = [
      ...new Set(items.map((i) => i.componentSKU).filter(Boolean)),
    ];

    const supplierMap = {};
    const componentMap = {};

    const supplierPromises = uniqueSupplierIds.map((id) =>
      $.ajax({ url: suppliersRoot + "/" + id, method: "GET" })
        .then((s) => {
          supplierMap[id] = s;
        })
        .catch(() => {}),
    );
    const componentPromises = uniqueSkus.map((sku) =>
      $.ajax({
        url: componentsRoot + "/" + encodeURIComponent(sku),
        method: "GET",
      })
        .then((c) => {
          componentMap[sku] = c;
        })
        .catch(() => {}),
    );

    const allPromises = supplierPromises.concat(componentPromises);
    const done =
      allPromises.length > 0 ? $.when.apply($, allPromises) : $.when();

    done.always(function () {
      const rows = items
        .map((item) => {
          const sku = item.componentSKU || "-";
          const comp = componentMap[item.componentSKU];
          const desc = comp && comp.description ? comp.description : "-";
          const supplier = supplierMap[item.supplierID];
          const suppName = supplier ? supplier.supplierName : "-";
          const suppUrl = supplier ? supplier.websiteUrl || "" : "";
          const suppCell = suppUrl
            ? `<a href="${suppUrl}" target="_blank" rel="noopener">${suppName}</a>`
            : suppName;
          const cost = parseFloat(item.costPriceMoment || 0).toFixed(2);
          const finalPrice = parseFloat(item.finalPriceToClient || 0).toFixed(
            2,
          );

          return `<tr>
                    <td>${sku}</td>
                    <td>${desc}</td>
                    <td>${item.quantity}</td>
                    <td>${suppCell}</td>
                    <td>${item.supplyConfig || "-"}</td>
                    <td>$${cost}</td>
                    <td>${item.profitMargin || 0}%</td>
                    <td><strong>$${finalPrice}</strong></td>
                </tr>`;
        })
        .join("");

      const total = items.reduce(
        (sum, i) => sum + parseFloat(i.finalPriceToClient || 0),
        0,
      );
      $("#quoteItemsBody").html(rows);
      $("#quoteItemsFoot").html(`<tr class="quote-items-total-row">
                <td colspan="7" style="text-align:left;font-weight:600;">סה"כ</td>
                <td><strong>$${total.toFixed(2)}</strong></td>
            </tr>`);
      showAlternativesBtn();
    });
  }

  // ── Draft/Processing quote: show raw search results ──
  function renderFromSearchResults(searchResultsJson) {
    try {
      const results = JSON.parse(searchResultsJson).results || [];
      if (results.length === 0) {
        $("#quoteItemsBody").html(
          '<tr><td colspan="8" class="table-empty">אין פריטים להצגה</td></tr>',
        );
        return;
      }

      const competitorNames = [
        "mouser",
        "digikey",
        "mouser electronics",
        "mouser electronics inc.",
      ];

      const rows = results
        .map((r) => {
          const allSupp = (r.suppliers || []).filter(
            (s) => !competitorNames.includes((s.name || "").toLowerCase()),
          );

          const cheapestSupp = allSupp.reduce(
            (best, s) =>
              s.unitPrice != null && (!best || s.unitPrice < best.unitPrice)
                ? s
                : best,
            null,
          );

          const desc =
            cheapestSupp?.description ||
            (r.competitors || [])[0]?.description ||
            "-";
          const suppName = cheapestSupp ? cheapestSupp.name : "N/A";
          const suppLink = cheapestSupp ? cheapestSupp.link || "" : "";
          const suppCell = suppLink
            ? `<a href="${suppLink}" target="_blank" rel="noopener">${suppName}</a>`
            : suppName;
          const price = cheapestSupp?.unitPrice;
          const priceDisplay = price != null ? "$" + price.toFixed(2) : "N/A";

          return `<tr>
                    <td>${r.sku || "-"}</td>
                    <td>${desc}</td>
                    <td>${r.qty || 0}</td>
                    <td>${suppCell}</td>
                    <td>-</td>
                    <td>${priceDisplay}</td>
                    <td>-</td>
                    <td>-</td>
                </tr>`;
        })
        .join("");

      $("#quoteItemsBody").html(rows);
      $("#quoteItemsFoot").html("");
      showAlternativesBtn();
    } catch (e) {
      console.error("Error parsing searchResultsJson:", e);
      $("#quoteItemsBody").html(
        '<tr><td colspan="8" class="table-empty">שגיאה בטעינת פריטים</td></tr>',
      );
    }
  }

  // ── Edit button ──
  $("#editQuoteBtn").click(function () {
    if (!quoteData) return;

    localStorage.setItem("quoteFromDraft", quoteData.quoteID);
    localStorage.setItem("quoteCustomerID", quoteData.customerID);
    localStorage.setItem(
      "quoteCustomerName",
      quoteData.customer ? quoteData.customer.customerName : "",
    );

    if (quoteData.searchResultsJson) {
      const results = JSON.parse(quoteData.searchResultsJson).results || [];
      localStorage.setItem(
        "quoteItems",
        JSON.stringify(
          results.map((r) => ({ sku: r.sku, qty: r.qty, config: "Reel" })),
        ),
      );
    } else if (quoteData.items && quoteData.items.length > 0) {
      localStorage.setItem(
        "quoteItems",
        JSON.stringify(
          quoteData.items.map((i) => ({
            sku: i.componentSKU,
            qty: i.quantity,
            config: i.supplyConfig || "Reel",
          })),
        ),
      );
    } else {
      alert("לא ניתן לערוך הצעה זו — אין פרטי פריטים");
      return;
    }

    window.location.href = "../quotes/new/step2.html";
  });

  function getQuoteECB(xhr, status, error) {
    console.log("Error loading quote:", status, error);
    alert("שגיאה בטעינת ההצעה");
  }

  // ── בדיקת חלופות ──
  const alternativesRoot = BASE_URL + "/api/AlternativeProd/quote";

  function showAlternativesBtn() {
    $("#checkAlternativesBtn").show();
  }

  $("#checkAlternativesBtn").click(function () {
    if (!quoteData) return;

    let items = [];

    if (quoteData.items && quoteData.items.length > 0) {
      items = quoteData.items.map((i) => ({
        componentSKU: i.componentSKU,
        quantity: i.quantity,
        supplierID: i.supplierID,
        supplyConfig: i.supplyConfig || "",
      }));
    } else if (quoteData.searchResultsJson) {
      const results = JSON.parse(quoteData.searchResultsJson).results || [];
      items = results.map((r) => ({
        componentSKU: r.sku,
        quantity: r.qty,
        supplierID: 0,
        supplyConfig: "",
      }));
    }

    if (items.length === 0) {
      alert("אין פריטים לבדיקה");
      return;
    }

    $("#recommendationsContent").html("<p>טוען המלצות...</p>");
    $("#recommendationsModal").show();

    ajaxCall(
      "POST",
      alternativesRoot,
      items,
      function (data) {
        if (!data.recommendations || data.recommendations.length === 0) {
          $("#recommendationsContent").html("<p>לא נמצאו חלופות כדאיות</p>");
          return;
        }
        let html = `<p><strong>${data.message}</strong></p>
            <table class="data-table"><thead><tr>
                <th>סוג</th><th>מק"ט מקורי</th><th>מק"ט מוצע</th>
                <th>מחיר מקורי</th><th>מחיר מוצע</th><th>חיסכון %</th><th>הסבר</th>
            </tr></thead><tbody>`;

        data.recommendations.forEach((r) => {
          html += `<tr>
                    <td>${r.recommendationType}</td>
                    <td>${r.originalSKU}</td>
                    <td>${r.suggestedSKU}</td>
                    <td>$${parseFloat(r.originalUnitPrice).toFixed(2)}</td>
                    <td>$${parseFloat(r.suggestedUnitPrice).toFixed(2)}</td>
                    <td>${r.savingPercent}%</td>
                    <td>${r.explanation}</td>
                </tr>`;
        });

        html += "</tbody></table>";
        $("#recommendationsContent").html(html);
      },
      function () {
        $("#recommendationsContent").html("<p>שגיאה בטעינת המלצות</p>");
      },
    );
  });

  // הוסיפי showAlternativesBtn() בשני המקומות הבאים:
});
