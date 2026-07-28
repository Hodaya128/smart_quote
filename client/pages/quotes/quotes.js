var allQuotes = [];
var exchangeRate = 1;
var currencySymbol = '$';
const apiRoot = BASE_URL + "/api/quotes";
var currentModalQuote = null;

function loadExchangeRate(callback) {
  const currency = localStorage.getItem('preferredCurrency') || 'USD';
  if (currency === 'USD') {
    exchangeRate = 1;
    currencySymbol = '$';
    callback();
    return;
  }
  const symbols = { ILS: '₪', EUR: '€', GBP: '£' };
  currencySymbol = symbols[currency] || currency;
  $.ajax({
    url: 'https://api.frankfurter.dev/v1/latest?base=USD&symbols=' + currency,
    method: 'GET',
    success: function (data) {
      exchangeRate = data.rates[currency] || 1;
      callback();
    },
    error: function () {
      exchangeRate = 1;
      callback();
    }
  });
}

$(document).ready(function () {

    // Show background message if exists
    const bgMsg = localStorage.getItem('bgMessage');
    if (bgMsg) {
        $('main').prepend(`<div class="bg-message" style="background:#fef3cd;border:1px solid #f0c36d;padding:12px 20px;border-radius:8px;margin-bottom:16px;color:#856404;font-weight:600;">${bgMsg}</div>`);
        localStorage.removeItem('bgMessage');
        setTimeout(() => $('.bg-message').fadeOut(), 5000);
    }

    loadExchangeRate(getQuotes);

    $('#searchInput').on('input', function () {
        const query = $(this).val().toLowerCase();
        const filtered = allQuotes.filter(q => q.customer && q.customer.customerName.toLowerCase().includes(query));
        renderQuotes(filtered);
    });

    function getQuotes() {
        $.ajax({
            url: apiRoot,
            method: 'GET',
            dataType: 'json',
            success: function (data) {
                allQuotes = data;
                if (!isAdminOrManager()) {
                    const myID = getUserID();
                    data = data.filter(q => q.createdBy === myID);
                }
                renderQuotes(data);
            },
            error: function (xhr, status, error) {
                console.log('Error loading JSON:', status, error);
                alert('בעיה בטעינה, נסה שוב מאוחר יותר');
            }
        });
    }


    function renderQuotes(data) {
        const container = $('#quotesContainer');
        container.empty();

        const rows = data.length === 0
            ? `<tr><td colspan="8" class="table-empty">אין הצעות מחיר</td></tr>`
            : data.map((q) => {
                let date = new Date(q.createdDate);
                let dateStr = date.getDate() + '/' + (date.getMonth() + 1) + '/' + date.getFullYear();

                // Action column based on status
                let actionCol = '';
                if (q.status === 'Processing') {
                    actionCol = '<span class="loading-spinner-small"></span>';
                } else if (q.status !== 'Completed') {
                    actionCol = `<button class="btn-choose-supplier btn-continue-edit" data-id="${q.quoteID}" onclick="event.stopPropagation();">המשך עריכה</button>`;
                }

                const clickable = q.status !== 'Processing';

                return `
            <tr style="cursor:${clickable ? 'pointer' : 'default'};" ${clickable ? `onclick="openQuoteModal(${q.quoteID})"` : ''}>
                <td>#${q.quoteID}</td>
                <td>${q.customer ? q.customer.customerName : ''}</td>
                <td>${dateStr}</td>
                <td>${currencySymbol + (parseFloat(q.finalTotalPrice) * exchangeRate).toFixed(2)}</td>
                <td>${currencySymbol + (parseFloat(q.totalProfit) * exchangeRate).toFixed(2)}</td>
                <td>${q.createdByName || '-'}</td>
                <td>${getStatusLabel(q.status)}</td>
                <td>${actionCol}</td>
                <td>
                    ${q.status !== 'Cancelled' && q.status !== 'Completed' ? `<button class="btn-cancel-quote" data-id="${q.quoteID}" onclick="event.stopPropagation();" style="background:none;border:1px solid #c0392b;color:#c0392b;border-radius:6px;padding:3px 10px;font-size:12px;cursor:pointer;">ביטול</button>` : ''}
                </td>
            </tr>
        `;
            }).join('');

        container.html(`
            <table class="data-table">
                <thead>
                    <tr>
                        <th>מספר הצעה</th>
                        <th>לקוח</th>
                        <th>תאריך</th>
                        <th>סכום</th>
                        <th>רווח</th>
                        <th>מתמחר</th>
                        <th>סטטוס</th>
                        <th>פעולות</th>
                        <th></th>
                    </tr>
                </thead>
                <tbody>${rows}</tbody>
            </table>
        `);

        // Continue editing
        $('.btn-continue-edit').click(function (e) {
            e.stopPropagation();
            const id = $(this).data('id');
            $.ajax({
                url: apiRoot + '/' + id,
                method: 'GET',
                success: function (quote) {
                    localStorage.setItem('quoteFromDraft',    id);
                    localStorage.setItem('quoteCustomerID',   quote.customerID);
                    localStorage.setItem('quoteCustomerName', quote.customer?.customerName || '');

                    if (quote.searchResultsJson) {
                        const results = JSON.parse(quote.searchResultsJson).results || [];
                        localStorage.setItem('quoteItems', JSON.stringify(
                            results.map(r => ({ sku: r.sku, qty: r.qty, config: 'Reel' }))
                        ));
                    } else if (quote.items && quote.items.length > 0) {
                        localStorage.setItem('quoteItems', JSON.stringify(
                            quote.items.map(i => ({
                                sku: i.componentSKU,
                                qty: i.quantity,
                                config: i.supplyConfig || 'Reel'
                            }))
                        ));
                    } else {
                        alert('לא ניתן לערוך הצעה זו — אין פרטי פריטים');
                        return;
                    }

                    window.location.href = 'new/step2.html';
                },
                error: function () { alert('שגיאה בטעינת ההצעה'); }
            });
        });

        // Cancel quote
        $('.btn-cancel-quote').click(function (e) {
            e.stopPropagation();
            const id = $(this).data('id');
            if (!confirm('האם אתה בטוח שברצונך לבטל את ההצעה?')) return;
            $.ajax({
                url: apiRoot + '/' + id + '/status',
                method: 'PATCH',
                contentType: 'application/json',
                data: JSON.stringify('Cancelled'),
                success: function () { getQuotes(); },
                error: function () { alert('שגיאה בביטול, נסה שוב'); }
            });
        });
    }

    // Close modal
    $('#closeQuoteModal, #quoteDetailModal').click(function (e) {
        if (e.target === this) $('#quoteDetailModal').hide();
    });

    // Export buttons
    $('#modalExportExcel').click(function () { exportQuoteExcel(); });
    $('#modalExportPdf').click(function () { exportQuotePdf(); });
});

function openQuoteModal(quoteID) {
    const suppliersRoot = BASE_URL + '/api/suppliers';
    const componentsRoot = BASE_URL + '/api/components';

    // Reset modal
    $('#modalItemsLoading').show();
    $('#modalItemsTable').hide();
    $('#modalItemsBody').empty();
    $('#modalItemsFoot').empty();
    $('#modalContinueEdit').hide();
    $('#quoteDetailModal').css('display', 'flex');

    $.ajax({
        url: BASE_URL + '/api/quotes/' + quoteID,
        method: 'GET',
        success: function (q) {
            currentModalQuote = q;
            const d = new Date(q.createdDate);
            const dateStr = d.getDate() + '/' + (d.getMonth() + 1) + '/' + d.getFullYear();

            $('#modalQuoteTitle').text('הצעה מספר #' + q.quoteID);
            $('#modalCustName').text(q.customer ? q.customer.customerName : '-');
            $('#modalQuoteDate').text(dateStr);
            $('#modalQuoteStatus').html(getStatusLabel(q.status));
            $('#modalQuoteTotal').text(currencySymbol + (parseFloat(q.finalTotalPrice) * exchangeRate).toFixed(2));
            $('#modalQuoteProfit').text(currencySymbol + (parseFloat(q.totalProfit) * exchangeRate).toFixed(2));
            $('#modalQuotePricer').text(q.createdByName || '-');

            // Show edit button if not completed
            if (q.status !== 'Completed' && q.status !== 'Processing') {
                $('#modalContinueEdit').show().off('click').on('click', function () {
                    localStorage.setItem('quoteFromDraft', q.quoteID);
                    localStorage.setItem('quoteCustomerID', q.customerID);
                    localStorage.setItem('quoteCustomerName', q.customer ? q.customer.customerName : '');
                    if (q.searchResultsJson) {
                        const results = JSON.parse(q.searchResultsJson).results || [];
                        localStorage.setItem('quoteItems', JSON.stringify(results.map(r => ({ sku: r.sku, qty: r.qty, config: 'Reel' }))));
                    } else if (q.items && q.items.length > 0) {
                        localStorage.setItem('quoteItems', JSON.stringify(q.items.map(i => ({ sku: i.componentSKU, qty: i.quantity, config: i.supplyConfig || 'Reel' }))));
                    } else {
                        alert('לא ניתן לערוך הצעה זו — אין פרטי פריטים');
                        return;
                    }
                    window.location.href = 'new/step2.html';
                });
            }

            if (q.items && q.items.length > 0) {
                renderModalItems(q.items, suppliersRoot, componentsRoot);
            } else if (q.searchResultsJson) {
                renderModalFromSearch(q.searchResultsJson);
            } else {
                $('#modalItemsLoading').hide();
                $('#modalItemsBody').html('<tr><td colspan="8" class="table-empty">אין פריטים להצגה</td></tr>');
                $('#modalItemsTable').show();
            }
        },
        error: function () { alert('שגיאה בטעינת ההצעה'); }
    });
}

function renderModalItems(items, suppliersRoot, componentsRoot) {
    const supplierMap = {}, componentMap = {};
    const supplierIds = [...new Set(items.map(i => i.supplierID).filter(Boolean))];
    const skus = [...new Set(items.map(i => i.componentSKU).filter(Boolean))];

    const promises = [];
    supplierIds.forEach(id => promises.push($.ajax({ url: suppliersRoot + '/' + id, method: 'GET' }).then(s => { supplierMap[id] = s; }).catch(() => {})));
    skus.forEach(sku => promises.push($.ajax({ url: componentsRoot + '/' + encodeURIComponent(sku), method: 'GET' }).then(c => { componentMap[sku] = c; }).catch(() => {})));

    (promises.length ? $.when.apply($, promises) : $.when()).always(function () {
        const rows = items.map(item => {
            const comp = componentMap[item.componentSKU];
            const supplier = supplierMap[item.supplierID];
            const suppName = supplier ? supplier.supplierName : '-';
            const suppUrl = supplier ? supplier.websiteUrl || '' : '';
            const suppCell = suppUrl ? `<a href="${suppUrl}" target="_blank">${suppName}</a>` : suppName;
            return `<tr>
                <td>${item.componentSKU || '-'}</td>
                <td>${comp && comp.description ? comp.description : '-'}</td>
                <td>${item.quantity}</td>
                <td>${suppCell}</td>
                <td>${item.supplyConfig || '-'}</td>
                <td>$${parseFloat(item.costPriceMoment || 0).toFixed(2)}</td>
                <td>${item.profitMargin || 0}%</td>
                <td><strong>$${parseFloat(item.finalPriceToClient || 0).toFixed(2)}</strong></td>
            </tr>`;
        }).join('');
        const total = items.reduce((sum, i) => sum + parseFloat(i.finalPriceToClient || 0), 0);
        $('#modalItemsBody').html(rows);
        $('#modalItemsFoot').html(`<tr><td colspan="7" style="text-align:left;font-weight:600;">סה"כ</td><td><strong>$${total.toFixed(2)}</strong></td></tr>`);
        $('#modalItemsLoading').hide();
        $('#modalItemsTable').show();
    });
}

function renderModalFromSearch(searchResultsJson) {
    try {
        const competitorNames = ['mouser', 'digikey', 'mouser electronics', 'mouser electronics inc.'];
        const results = JSON.parse(searchResultsJson).results || [];
        if (results.length === 0) {
            $('#modalItemsLoading').hide();
            $('#modalItemsBody').html('<tr><td colspan="8" class="table-empty">אין פריטים להצגה</td></tr>');
            $('#modalItemsTable').show();
            return;
        }
        const rows = results.map(r => {
            const allSupp = (r.suppliers || []).filter(s => !competitorNames.includes((s.name || '').toLowerCase()));
            const cheapest = allSupp.reduce((best, s) => s.unitPrice != null && (!best || s.unitPrice < best.unitPrice) ? s : best, null);
            const desc = cheapest?.description || (r.competitors || [])[0]?.description || '-';
            const suppName = cheapest ? cheapest.name : 'N/A';
            const suppLink = cheapest ? cheapest.link || '' : '';
            const suppCell = suppLink ? `<a href="${suppLink}" target="_blank">${suppName}</a>` : suppName;
            const price = cheapest?.unitPrice;
            return `<tr>
                <td>${r.sku || '-'}</td>
                <td>${desc}</td>
                <td>${r.qty || 0}</td>
                <td>${suppCell}</td>
                <td>-</td>
                <td>${price != null ? '$' + price.toFixed(2) : 'N/A'}</td>
                <td>-</td>
                <td>-</td>
            </tr>`;
        }).join('');
        $('#modalItemsBody').html(rows);
        $('#modalItemsLoading').hide();
        $('#modalItemsTable').show();
    } catch (e) {
        $('#modalItemsLoading').text('שגיאה בטעינת פריטים');
    }
}

function getModalExportRows() {
    const q = currentModalQuote;
    if (!q) return { rows: [], customerName: '', dateStr: '', quoteID: '' };

    const d = new Date(q.createdDate);
    const dateStr = d.getDate() + '/' + (d.getMonth() + 1) + '/' + d.getFullYear();
    const customerName = q.customer ? q.customer.customerName : '';
    const competitorNames = ['mouser', 'digikey', 'mouser electronics', 'mouser electronics inc.'];

    let rows = [];

    if (q.items && q.items.length > 0) {
        rows = q.items.map(i => ({
            sku: i.componentSKU || '-',
            desc: '-',
            qty: i.quantity,
            config: i.supplyConfig || '-',
            unitPrice: parseFloat(i.costPriceMoment || 0),
            finalPrice: parseFloat(i.finalPriceToClient || 0),
            margin: (i.profitMargin || 0) + '%'
        }));
    } else if (q.searchResultsJson) {
        const results = JSON.parse(q.searchResultsJson).results || [];
        rows = results.map(r => {
            const allSupp = (r.suppliers || []).filter(s => !competitorNames.includes((s.name || '').toLowerCase()));
            const cheapest = allSupp.reduce((best, s) => s.unitPrice != null && (!best || s.unitPrice < best.unitPrice) ? s : best, null);
            return {
                sku: r.sku || '-',
                desc: cheapest?.description || '-',
                qty: r.qty || 0,
                config: '-',
                unitPrice: cheapest?.unitPrice || 0,
                finalPrice: cheapest?.unitPrice ? cheapest.unitPrice * (r.qty || 0) : 0,
                margin: '-'
            };
        });
    }

    return { rows, customerName, dateStr, quoteID: q.quoteID };
}

function exportQuoteExcel() {
    const { rows, customerName, dateStr, quoteID } = getModalExportRows();
    if (!rows.length) { alert('אין נתונים לייצוא'); return; }

    const wsData = [
        ['Quote #' + quoteID],
        ['Customer: ' + customerName],
        ['Date: ' + dateStr],
        [],
        ['SKU', 'Description', 'Quantity', 'Supply Config', 'Unit Price ($)', 'Total ($)', 'Margin']
    ];
    let grandTotal = 0;
    rows.forEach(r => {
        wsData.push([r.sku, r.desc, r.qty, r.config, r.unitPrice, r.finalPrice, r.margin]);
        grandTotal += r.finalPrice;
    });
    wsData.push([]);
    wsData.push(['', '', '', '', '', 'Total: $' + grandTotal.toFixed(2)]);

    const wb = XLSX.utils.book_new();
    const ws = XLSX.utils.aoa_to_sheet(wsData);
    ws['!cols'] = [{ wch: 18 }, { wch: 30 }, { wch: 10 }, { wch: 14 }, { wch: 14 }, { wch: 14 }, { wch: 10 }];
    XLSX.utils.book_append_sheet(wb, ws, 'Quote');
    XLSX.writeFile(wb, 'Quote_' + quoteID + '_' + customerName.replace(/\s/g, '_') + '.xlsx');
}

function exportQuotePdf() {
    const { rows, customerName, dateStr, quoteID } = getModalExportRows();
    if (!rows.length) { alert('אין נתונים לייצוא'); return; }

    const { jsPDF } = window.jspdf;
    const doc = new jsPDF({ orientation: 'portrait', unit: 'mm', format: 'a4' });
    const pageWidth = doc.internal.pageSize.getWidth();

    doc.setFontSize(16);
    doc.setFont('helvetica', 'bold');
    doc.text('Quote #' + quoteID, 14, 20);

    doc.setFontSize(11);
    doc.setFont('helvetica', 'normal');
    doc.text('Customer: ' + customerName, 14, 30);
    doc.text('Date: ' + dateStr, 14, 37);

    let grandTotal = 0;
    const tableRows = rows.map(r => {
        grandTotal += r.finalPrice;
        return [r.sku, r.desc, r.qty, r.config, '$' + r.unitPrice.toFixed(2), '$' + r.finalPrice.toFixed(2), r.margin];
    });

    doc.autoTable({
        startY: 45,
        head: [['SKU', 'Description', 'Qty', 'Config', 'Unit Price', 'Total', 'Margin']],
        body: tableRows,
        styles: { fontSize: 9, cellPadding: 3 },
        headStyles: { fillColor: [91, 127, 166], textColor: 255, fontStyle: 'bold' },
        alternateRowStyles: { fillColor: [245, 248, 252] },
        foot: [['', '', '', '', '', 'Grand Total', '$' + grandTotal.toFixed(2)]],
        footStyles: { fillColor: [230, 230, 230], fontStyle: 'bold' }
    });

    doc.save('Quote_' + quoteID + '_' + customerName.replace(/\s/g, '_') + '.pdf');
}
