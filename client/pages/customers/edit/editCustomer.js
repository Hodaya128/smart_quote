const apiRoot = BASE_URL + '/api/customers';
let id;

$(document).ready(function () {
    loadCustomer();

    $('#editCustomerForm').submit(function (e) {
        e.preventDefault();
        setLoading(true);

        const body = {
            customerName: $('#customerName').val(),
            email: $('#email').val(),
            phone: $('#phone').val(),
            address: $('#address').val()
        };

        ajaxCall("PUT", apiRoot + '/' + id, JSON.stringify(body), editCustomerSCB, editCustomerECB);
    });
});

function loadCustomer() {
    id = new URLSearchParams(window.location.search).get('id');
    if (!id) { alert('מזהה לקוח חסר'); return; }
    ajaxCall("GET", apiRoot + '/' + id, "", loadCustomerSCB, loadCustomerECB);
}

function loadCustomerSCB(data) {
    $('#customerName').val(data.customerName);
    $('#email').val(data.email);
    $('#phone').val(data.phone);
    $('#address').val(data.address);
}

function loadCustomerECB(xhr, status, error) {
    console.log('Error loading customer:', status, error);
    alert('שגיאה בטעינת הלקוח');
}

function setLoading(on) {
    $('#saveBtn').toggle(!on);
    $('#loader').toggleClass('active', on);
}

function editCustomerSCB() {
    window.location.href = '/pages/customers';
}

function editCustomerECB(xhr, status, err) {
    console.log('Error updating:', status, err);
    alert('שגיאה בעדכון, נסה שוב');
    setLoading(false);
}
