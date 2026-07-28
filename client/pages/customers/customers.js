let allCustomers = [];
const apiRoot = BASE_URL + '/api/customers';
const quotesApiRoot = BASE_URL + '/api/quotes';

$(document).ready(function () {
    getCustomers();

    $('#searchInput').on('input', function () {
        const query = $(this).val().toLowerCase();
        const filtered = allCustomers.filter(c => c.customerName.toLowerCase().includes(query));
        renderCustomers(filtered);
    });

    function getCustomers() {
        ajaxCall("GET", apiRoot, "", getCustomersSCB, getCustomersECB);
    }

    function getCustomersSCB(data) {
        allCustomers = data;
        renderCustomers(data);
    }

    function getCustomersECB(xhr, status, error) {
        console.log('Error loading JSON:', status, error);
        alert('בעיה בטעינה, נסה שוב מאוחר יותר');
    }

    function renderCustomers(data) {
        const customersContainer = $('#customersContainer');
        customersContainer.empty();

        const rows = data.length === 0
            ? `<tr><td colspan="6" class="table-empty">אין לקוחות</td></tr>`
            : data.map(c => `
                <tr style="cursor:pointer;" onclick="openCustomerModal(allCustomers.find(c=>c.customerID===${c.customerID}), 'view')">
                    <td>${c.customerID}</td>
                    <td><img src="../../images/header/customers.png" class="row-icon" alt=""/> ${c.customerName}</td>
                    <td>${c.email}</td>
                    <td>${c.phone}</td>
                    <td>${c.address || '-'}</td>
                    <td>
                        <img src="../../images/edit.png" class="icon-edit" data-id="${c.customerID}" alt="עריכה" style="cursor:pointer;" onclick="event.stopPropagation(); openCustomerModal(allCustomers.find(c=>c.customerID===${c.customerID}), 'edit');" />
                        <img src="../../images/trash.png" class="icon-delete" data-id="${c.customerID}" alt="מחק" onclick="event.stopPropagation();" />
                    </td>
                </tr>
            `).join('');

        customersContainer.html(`
            <table class="data-table">
                <thead>
                    <tr>
                        <th>#</th>
                        <th>שם לקוח</th>
                        <th>אימייל</th>
                        <th>טלפון</th>
                        <th>כתובת</th>
                        <th></th>
                    </tr>
                </thead>
                <tbody>${rows}</tbody>
            </table>
        `);

        $('.icon-delete').click(function () {
            const id = $(this).data('id');
            if (!confirm('האם אתה בטוח שברצונך למחוק את הלקוח?')) return;
            ajaxCall("DELETE", apiRoot + '/' + id, "", deleteCustomerSCB, deleteCustomerECB);
        });

    }

    function deleteCustomerSCB() {
        getCustomers();
    }

    function deleteCustomerECB(xhr, status, error) {
        console.log('Error deleting:', status, error);
        alert('שגיאה במחיקה, נסה שוב');
    }

    $('#closeCustomerModal').click(function () {
        $('#customerModal').hide();
    });

    $('#customerModal').click(function (e) {
        if ($(e.target).is('#customerModal')) $('#customerModal').hide();
    });

    $('#modalEditCustomerForm').submit(function (e) {
        e.preventDefault();
        const id = $('#modalEditCustomerID').val();
        const body = {
            customerName: $('#modalEditCustomerName').val(),
            email: $('#modalEditCustomerEmail').val(),
            phone: $('#modalEditCustomerPhone').val(),
            address: $('#modalEditCustomerAddress').val()
        };
        ajaxCall("PUT", apiRoot + '/' + id, body, function () {
            $('#modalEditSuccess').show();
            getCustomers();
        }, function () { alert('שגיאה בשמירה, נסה שוב'); });
    });
});

function openCustomerModal(customer, mode) {
    if (!customer) return;

    if (mode === 'edit') {
        $('#custViewSection').hide();
        $('#custEditSection').show();
        $('.cust-quotes-section').hide();
        $('#customerModalTitle').text('עריכת לקוח');
        $('#modalEditCustomerID').val(customer.customerID);
        $('#modalEditCustomerName').val(customer.customerName || '');
        $('#modalEditCustomerEmail').val(customer.email || '');
        $('#modalEditCustomerPhone').val(customer.phone || '');
        $('#modalEditCustomerAddress').val(customer.address || '');
        $('#modalEditSuccess').hide();
        $('#customerModal').show();
        return;
    } else {
        $('#custEditSection').hide();
        $('#custViewSection').show();
        $('.cust-quotes-section').show();
        $('#customerModalTitle').text('פרטי לקוח');
        $('#modalContactName').text(customer.customerName || '-');
        $('#modalEmail').text(customer.email || '-');
        $('#modalPhone').text(customer.phone || '-');
        $('#modalAddress').text(customer.address || '-');
    }

    $('#modalQuotesContainer').html('<p style="color:#aaa;font-size:14px;">טוען הצעות מחיר...</p>');
    $('#customerModal').show();

    ajaxCall("GET", quotesApiRoot, "", function (allQuotes) {
        const customerQuotes = allQuotes.filter(q => q.customerID === customer.customerID);
        renderModalQuotes(customerQuotes);
    }, function () {
        $('#modalQuotesContainer').html('<p style="color:#c0392b;font-size:14px;">שגיאה בטעינת הצעות מחיר</p>');
    });
}

function renderModalQuotes(quotes) {
    if (quotes.length === 0) {
        $('#modalQuotesContainer').html('<p class="cust-no-quotes">אין הצעות מחיר משויכות ללקוח זה</p>');
        return;
    }

    const rows = quotes.map(q => {
        const d = new Date(q.createdDate);
        const dateStr = d.getDate() + '/' + (d.getMonth() + 1) + '/' + d.getFullYear();
        const editBtn = (q.status !== 'Completed' && q.status !== 'Processing' && q.searchResultsJson)
            ? `<button class="cust-edit-btn" data-id="${q.quoteID}">המשך עריכה</button>`
            : '';
        return `
            <tr>
                <td><a class="cust-quote-link" href="/pages/quote/?quoteID=${q.quoteID}">#${q.quoteID}</a></td>
                <td>${dateStr}</td>
                <td>$${parseFloat(q.finalTotalPrice).toFixed(2)}</td>
                <td>$${parseFloat(q.totalProfit).toFixed(2)}</td>
                <td>${getStatusLabel(q.status)}</td>
                <td>${editBtn}</td>
            </tr>`;
    }).join('');

    $('#modalQuotesContainer').html(`
        <table class="cust-quotes-table">
            <thead>
                <tr>
                    <th>מספר הצעה</th>
                    <th>תאריך</th>
                    <th>סכום</th>
                    <th>רווח</th>
                    <th>סטטוס</th>
                    <th></th>
                </tr>
            </thead>
            <tbody>${rows}</tbody>
        </table>
    `);

    $('.cust-edit-btn').click(function () {
        const id    = $(this).data('id');
        const quote = quotes.find(q => q.quoteID === id);
        if (!quote) return;

        localStorage.setItem('quoteFromDraft',    quote.quoteID);
        localStorage.setItem('quoteCustomerID',   quote.customerID);
        localStorage.setItem('quoteCustomerName', quote.customer ? quote.customer.customerName : '');

        try {
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
            window.location.href = '/pages/quotes/new/step2.html';
        } catch (e) {
            alert('שגיאה בטעינת ההצעה לעריכה');
        }
    });
}
