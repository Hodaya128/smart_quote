var allSuppliers = [];
const apiRoot = BASE_URL + '/api/suppliers';

$(document).ready(function () {
    getSuppliers();

    $('#searchInput').on('input', function () {
        const query = $(this).val().toLowerCase();
        const filtered = allSuppliers.filter(s => s.supplierName.toLowerCase().includes(query));
        renderSuppliers(filtered);
    });

    function getSuppliers() {
        ajaxCall("GET", apiRoot, "", getSuppliersSCB, getSuppliersECB);
    }

    function getSuppliersSCB(data) {
        allSuppliers = data;
        renderSuppliers(data);
    }

    function getSuppliersECB(xhr, status, error) {
        console.log('Error loading JSON:', status, error);
        alert('בעיה בטעינה, נסה שוב מאוחר יותר');
    }

    function deleteSupplierSCB() {
        getSuppliers();
    }

    function renderSuppliers(data) {
        const suppliersContainer = $('#suppliersContainer');
        suppliersContainer.empty();

        const rows = data.length === 0
            ? `<tr><td colspan="4" class="table-empty">אין ספקים</td></tr>`
            : data.map(supplier => `
            <tr>
                <td>${supplier.supplierID}</td>
                <td><img src="../../images/header/suppliers.png" class="row-icon" alt=""/> ${supplier.supplierName}</td>
                <td><a href="${supplier.websiteUrl}" target="_blank">${supplier.websiteUrl}</a></td>
                <td>
                    <img src="../../images/edit.png" class="icon-edit" data-id="${supplier.supplierID}" title="עריכה" style="cursor:pointer;" />
                    <img src="../../images/trash.png" class="icon-delete" data-id="${supplier.supplierID}" title="מחק ספק" />
                </td>
            </tr>
        `).join('');

        suppliersContainer.html(`
            <table class="data-table">
                <thead>
                    <tr>
                        <th>#</th>
                        <th>שם ספק</th>
                        <th>אתר אינטרנט</th>
                        <th></th>
                    </tr>
                </thead>
                <tbody>${rows}</tbody>
            </table>
        `);

        $('.icon-delete').click(function () {
            const id = $(this).data('id');
            if (!confirm('האם אתה בטוח שברצונך למחוק את הספק?')) return;
            ajaxCall("DELETE", apiRoot + '/' + id, "", deleteSupplierSCB, deleteSupplierECB);
        });

        $('.icon-edit').click(function () {
            const id = $(this).data('id');
            const supplier = allSuppliers.find(s => s.supplierID === id);
            if (!supplier) return;
            $('#editSupplierID').val(supplier.supplierID);
            $('#editSupplierName').val(supplier.supplierName);
            $('#editWebsiteUrl').val(supplier.websiteUrl);
            $('#editSupplierModal').addClass('active');
        });
    }

    function deleteSupplierECB(xhr, status, error) {
        console.log('Error deleting:', status, error);
        alert('שגיאה במחיקה, נסה שוב');
    }

    $('#closeSupplierModal, #editSupplierModal').click(function (e) {
        if (e.target === this) $('#editSupplierModal').removeClass('active');
    });

    $('#editSupplierForm').submit(function (e) {
        e.preventDefault();
        const id = $('#editSupplierID').val();
        const body = {
            supplierName: $('#editSupplierName').val(),
            websiteUrl: $('#editWebsiteUrl').val()
        };
        ajaxCall("PUT", apiRoot + '/' + id, body, function () {
            $('#editSupplierModal').removeClass('active');
            getSuppliers();
        }, function () { alert('שגיאה בשמירה, נסה שוב'); });
    });
});
